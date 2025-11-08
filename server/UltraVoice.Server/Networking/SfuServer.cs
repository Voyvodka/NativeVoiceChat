using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using MessagePack;
using Microsoft.Extensions.Logging;
using UltraVoice.Shared.Audio;
using UltraVoice.Shared.Configuration;
using UltraVoice.Shared.Wire;

namespace UltraVoice.Server.Networking;

/// <summary>
/// Lightweight SFU loop responsible for relaying Opus frames and control messages.
/// The implementation is intentionally minimal, focusing on a single UDP port.
/// </summary>
public sealed class SfuServer : INetEventListener, IDisposable
{
    private readonly ILogger<SfuServer> _logger;
    private readonly ServerConfig _config;
    private readonly NetManager _netManager;
    private readonly Dictionary<NetPeer, Session> _sessions = new(NetPeerComparer.Instance);
    private readonly Dictionary<string, HashSet<NetPeer>> _rooms;
    private readonly object _gate = new();

    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TimeoutInterval = TimeSpan.FromSeconds(15);
    private const int ControlRateLimitPerSecond = 10;
    private const int AudioRateLimitPerSecond = 80;

    private uint _nextSessionId = RandomSessionId();
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;
    private uint[] _activeSpeakers = Array.Empty<uint>();
    private DateTimeOffset _lastMaintenance = DateTimeOffset.MinValue;

    public SfuServer(ServerConfig config, ILogger<SfuServer> logger)
    {
        _config = config;
        _logger = logger;
        _rooms = _config.Rooms.ToDictionary(room => room, _ => new HashSet<NetPeer>());
        _netManager = new NetManager(this)
        {
            IPv6Enabled = false,
            NatPunchEnabled = false,
            UnconnectedMessagesEnabled = false
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_netManager.Start(_config.Port))
        {
            throw new InvalidOperationException($"Failed to start NetManager at UDP:{_config.Port}");
        }

        _logger.LogInformation("UltraVoice SFU listening on UDP:{Port}", _config.Port);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pumpTask = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        if (_pumpTask is { } pump)
        {
            return FinishStopAsync(pump, cancellationToken);
        }

        _netManager.Stop();
        lock (_gate)
        {
            foreach (var peers in _rooms.Values)
            {
                peers.Clear();
            }

            _sessions.Clear();
        }

        return Task.CompletedTask;
    }

    private async Task FinishStopAsync(Task pump, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            await Task.WhenAny(pump, Task.Delay(Timeout.Infinite, linked.Token));
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        finally
        {
            _netManager.Stop();
            lock (_gate)
            {
                foreach (var peers in _rooms.Values)
                {
                    peers.Clear();
                }

                _sessions.Clear();
            }
            _pumpTask = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        if (_netManager.ConnectedPeersCount >= _config.MaxUsersPerRoom * _rooms.Count)
        {
            request.Reject();
            return;
        }

        request.Accept();
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        _logger.LogWarning("Network error from {EndPoint}: {Error}", endPoint, socketError);
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        if (_sessions.TryGetValue(peer, out var session))
        {
            session.LatencyMs = latency;
        }
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        _logger.LogInformation("Peer {Address} connected", peer);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(peer, out var session))
            {
                if (_rooms.TryGetValue(session.RoomId, out var peers))
                {
                    peers.Remove(peer);
                }

                _sessions.Remove(peer);
                BroadcastState(session.RoomId);
            }
        }

        _logger.LogInformation("Peer {Address} disconnected ({Reason})", peer, disconnectInfo.Reason);
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        try
        {
            var envelope = MessagePackSerializer.Deserialize<MessageEnvelope>(reader.GetRemainingBytes());
            HandleEnvelope(peer, envelope);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize control payload from {Peer}", peer);
        }
        finally
        {
            reader.Recycle();
        }
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        // MVP discards unconnected messages; future TCP/handshake work can extend this.
        reader.Recycle();
    }

    private void HandleEnvelope(NetPeer peer, MessageEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case MessageType.Hello:
                HandleHello(peer, envelope.Payload);
                break;
            case MessageType.AudioFrame:
                RelayAudio(peer, envelope);
                break;
            case MessageType.Ping:
                RespondPing(peer, envelope);
                break;
            case MessageType.UserEvent:
                UpdateUserEvent(peer, envelope.Payload);
                break;
            default:
                _logger.LogDebug("Unhandled message type {Type}", envelope.Type);
                break;
        }
    }

    private void HandleHello(NetPeer peer, byte[] data)
    {
        HelloMessage hello;

        try
        {
            hello = MessagePackSerializer.Deserialize<HelloMessage>(data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid HELLO payload from {Peer}", peer);
            peer.Disconnect();
            return;
        }

        if (_config.SharedToken is { Length: > 0 } token && !string.Equals(token, hello.Token, StringComparison.Ordinal))
        {
            _logger.LogInformation("Peer {Peer} rejected due to token mismatch", peer);
            peer.Disconnect();
            return;
        }

        if (!_rooms.ContainsKey(hello.RoomId))
        {
            _logger.LogInformation("Peer {Peer} requested unknown room {Room}, defaulting to room-a", peer, hello.RoomId);
            hello = new HelloMessage
            {
                Username = hello.Username,
                RoomId = "room-a",
                Token = hello.Token
            };
        }

        lock (_gate)
        {
            if (!_rooms.TryGetValue(hello.RoomId, out var peers))
            {
                peer.Disconnect();
                return;
            }

            var previousRoom = string.Empty;
            if (_sessions.TryGetValue(peer, out var existingSession) && existingSession is not null)
            {
                previousRoom = existingSession.RoomId ?? string.Empty;
            }

            var joiningNewRoom = previousRoom != hello.RoomId;

            if (joiningNewRoom && peers.Count >= _config.MaxUsersPerRoom)
            {
                _logger.LogInformation("Room {Room} full, rejecting {Peer}", hello.RoomId, peer);
                if (!string.IsNullOrEmpty(previousRoom) && _rooms.TryGetValue(previousRoom, out var previousPeers))
                {
                    previousPeers.Add(peer);
                }
                peer.Disconnect();
                return;
            }

            Session session;
            var now = DateTimeOffset.UtcNow;
            if (!_sessions.TryGetValue(peer, out var retrieved) || retrieved is null)
            {
                session = new Session
                {
                    SessionId = NextSessionId(),
                    Username = hello.Username,
                    RoomId = hello.RoomId,
                    ConnectedAt = now,
                    LastControlAt = now,
                    LastActivityAt = now,
                    ControlWindowStart = now,
                    ControlMessagesThisWindow = 0,
                    AudioWindowStart = now,
                    AudioMessagesThisWindow = 0,
                    LastAudioAt = now
                };
                _sessions[peer] = session;
            }
            else
            {
                session = retrieved;
                if (joiningNewRoom && _rooms.TryGetValue(previousRoom, out var previousPeers))
                {
                    previousPeers.Remove(peer);
                }

                session.Username = hello.Username;
                session.RoomId = hello.RoomId;
                session.LastControlAt = now;
                session.LastActivityAt = now;
                session.ControlWindowStart = now;
                session.ControlMessagesThisWindow = 0;
                session.AudioWindowStart = now;
                session.AudioMessagesThisWindow = 0;
                session.LastAudioAt = now;
            }

            peers.Add(peer);

            SendWelcome(peer, session);
            BroadcastState(session.RoomId);
            if (joiningNewRoom && !string.IsNullOrEmpty(previousRoom))
            {
                BroadcastState(previousRoom);
            }
        }
    }

    private void SendWelcome(NetPeer peer, Session session)
    {
        var welcome = new WelcomeMessage
        {
            SessionId = session.SessionId,
            AvailableRooms = _rooms.Keys.ToArray()
        };

        SendEnvelope(peer, MessageType.Welcome, session.SessionId, MessagePackSerializer.Serialize(welcome));
    }

    private void RelayAudio(NetPeer sender, MessageEnvelope envelope)
    {
        AudioFrameMessage frame;
        try
        {
            frame = MessagePackSerializer.Deserialize<AudioFrameMessage>(envelope.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid AUDIO_FRAME payload from {Peer}", sender);
            return;
        }

        byte[] serializedEnvelope;
        Session? session;
        bool activeChanged = false;

        lock (_gate)
        {
            if (!_sessions.TryGetValue(sender, out session))
            {
                return;
            }

            if (!TryAllowAudio(session))
            {
                LogRateLimit(sender, "audio");
                return;
            }

            session.LastRms = frame.Rms;

            var forwardedEnvelope = new MessageEnvelope
            {
                Type = MessageType.AudioFrame,
                SessionId = session.SessionId,
                Sequence = envelope.Sequence,
                TimestampMs = envelope.TimestampMs,
                Payload = envelope.Payload
            };

            serializedEnvelope = MessagePackSerializer.Serialize(forwardedEnvelope);

            if (!_rooms.TryGetValue(session.RoomId, out var peers))
            {
                return;
            }

            foreach (var peer in peers)
            {
                if (peer == sender)
                {
                    continue;
                }

                peer.Send(serializedEnvelope, DeliveryMethod.Sequenced);
            }

            activeChanged = RecalculateActiveSpeakersLocked();
        }

        if (activeChanged)
        {
            BroadcastStateToAll();
        }
    }

    private void RespondPing(NetPeer peer, MessageEnvelope envelope)
    {
        if (!TryGetSession(peer, out var session) || session is null)
        {
            return;
        }

        if (!TryAllowControl(session))
        {
            LogRateLimit(peer, "control");
            return;
        }

        SendEnvelope(peer, MessageType.Pong, session.SessionId, envelope.Payload);
    }

    private void UpdateUserEvent(NetPeer peer, byte[] payload)
    {
        if (!_sessions.TryGetValue(peer, out var session))
        {
            return;
        }

        if (!TryAllowControl(session))
        {
            LogRateLimit(peer, "control");
            return;
        }

        try
        {
            var evt = MessagePackSerializer.Deserialize<UserEventMessage>(payload);
            session.IsMuted = evt.Mute ?? session.IsMuted;
            session.VolumeDb = evt.VolumeDb ?? session.VolumeDb;
            BroadcastState(session.RoomId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse USER_EVENT from {Peer}", peer);
        }
    }

    private void BroadcastState(string roomId)
    {
        var state = BuildStateSnapshot();
        var envelope = new MessageEnvelope
        {
            Type = MessageType.State,
            SessionId = 0,
            Sequence = 0,
            TimestampMs = CurrentTimestamp(),
            Payload = MessagePackSerializer.Serialize(state)
        };

        var bytes = MessagePackSerializer.Serialize(envelope);

        if (!_rooms.TryGetValue(roomId, out var peers))
        {
            return;
        }

        foreach (var peer in peers)
        {
            peer.Send(bytes, DeliveryMethod.ReliableOrdered);
        }
    }

    private void BroadcastStateToAll()
    {
        var state = BuildStateSnapshot();
        var envelope = new MessageEnvelope
        {
            Type = MessageType.State,
            SessionId = 0,
            Sequence = 0,
            TimestampMs = CurrentTimestamp(),
            Payload = MessagePackSerializer.Serialize(state)
        };

        var bytes = MessagePackSerializer.Serialize(envelope);

        foreach (var peers in _rooms.Values)
        {
            foreach (var peer in peers)
            {
                peer.Send(bytes, DeliveryMethod.ReliableOrdered);
            }
        }
    }

    private StateMessage BuildStateSnapshot()
    {
        lock (_gate)
        {
            RecalculateActiveSpeakersLocked();
            var rooms = _rooms.Select(room =>
            {
                var users = room.Value
                    .Select(peer => _sessions.TryGetValue(peer, out var session) ? session : null)
                    .Where(session => session is not null && string.Equals(session.RoomId, room.Key, StringComparison.Ordinal))
                    .Select(session => new UserSummary
                    {
                        SessionId = session!.SessionId,
                        Username = session.Username,
                        JoinedAtUnixMs = (ulong)session.ConnectedAt.ToUnixTimeMilliseconds(),
                        IsMuted = session.IsMuted,
                        VolumeDb = session.VolumeDb
                    })
                    .ToArray();

                return new RoomState
                {
                    RoomId = room.Key,
                    Users = users
                };
            }).ToArray();

            return new StateMessage
            {
                Rooms = rooms,
                ActiveSpeakers = _activeSpeakers
            };
        }
    }

    private bool RecalculateActiveSpeakersLocked()
    {
        var top = _sessions.Values
            .Where(s => !s.IsMuted)
            .OrderByDescending(s => s.LastRms)
            .ThenByDescending(s => s.LastAudioAt)
            .Take(2)
            .Select(s => s.SessionId)
            .ToArray();

        if (_activeSpeakers.SequenceEqual(top))
        {
            return false;
        }

        _activeSpeakers = top;
        return true;
    }

    private bool TryGetSession(NetPeer peer, out Session? session)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(peer, out session);
        }
    }

    private static bool TryAllowControl(Session session)
    {
        var now = DateTimeOffset.UtcNow;
        if (session.ControlWindowStart == DateTimeOffset.MinValue || now - session.ControlWindowStart >= TimeSpan.FromSeconds(1))
        {
            session.ControlWindowStart = now;
            session.ControlMessagesThisWindow = 0;
        }

        if (session.ControlMessagesThisWindow >= ControlRateLimitPerSecond)
        {
            return false;
        }

        session.ControlMessagesThisWindow++;
        session.LastControlAt = now;
        session.LastActivityAt = now;
        return true;
    }

    private static bool TryAllowAudio(Session session)
    {
        var now = DateTimeOffset.UtcNow;
        if (session.AudioWindowStart == DateTimeOffset.MinValue || now - session.AudioWindowStart >= TimeSpan.FromSeconds(1))
        {
            session.AudioWindowStart = now;
            session.AudioMessagesThisWindow = 0;
        }

        if (session.AudioMessagesThisWindow >= AudioRateLimitPerSecond)
        {
            return false;
        }

        session.AudioMessagesThisWindow++;
        session.LastAudioAt = now;
        session.LastActivityAt = now;
        return true;
    }

    private void LogRateLimit(NetPeer peer, string category)
        => _logger.LogWarning("Peer {Peer} exceeded {Category} rate limit", peer, category);

    private static void SendEnvelope(NetPeer peer, MessageType type, uint sessionId, byte[] payload)
    {
        var envelope = new MessageEnvelope
        {
            Type = type,
            SessionId = sessionId,
            Sequence = 0,
            TimestampMs = CurrentTimestamp(),
            Payload = payload
        };

        peer.Send(MessagePackSerializer.Serialize(envelope), DeliveryMethod.ReliableOrdered);
    }

    private static void SendPing(NetPeer peer, uint sessionId)
    {
        var payload = BitConverter.GetBytes(CurrentTimestamp());
        SendEnvelope(peer, MessageType.Ping, sessionId, payload);
    }

    private static uint NextSessionId(ref uint nextId)
    {
        if (++nextId == 0)
        {
            nextId = 1;
        }

        return nextId;
    }

    private uint NextSessionId()
    {
        return NextSessionId(ref _nextSessionId);
    }

    private static uint RandomSessionId()
    {
        Span<byte> buffer = stackalloc byte[4];
        Random.Shared.NextBytes(buffer);
        return BitConverter.ToUInt32(buffer);
    }

    private static uint CurrentTimestamp()
        => (uint)Environment.TickCount64;

    public void Dispose()
    {
        _cts?.Cancel();
        _pumpTask?.GetAwaiter().GetResult();
        _netManager.Stop();
    }

    private void TickMaintenance()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastMaintenance < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _lastMaintenance = now;
        List<(NetPeer Peer, uint SessionId)> pingList = [];
        List<NetPeer> disconnectList = [];

        lock (_gate)
        {
            foreach (var (peer, session) in _sessions)
            {
                var idle = now - session.LastActivityAt;
                if (idle >= TimeoutInterval)
                {
                    disconnectList.Add(peer);
                }
                else if (idle >= KeepAliveInterval && now - session.LastPingSentAt >= KeepAliveInterval)
                {
                    session.LastPingSentAt = now;
                    pingList.Add((peer, session.SessionId));
                }
            }
        }

        foreach (var (peer, sessionId) in pingList)
        {
            SendPing(peer, sessionId);
        }

        foreach (var peer in disconnectList)
        {
            peer.Disconnect();
        }
    }

    private async Task PumpAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            _netManager.PollEvents();
            TickMaintenance();
            await Task.Delay(10, token).ConfigureAwait(false);
        }
    }

    private sealed class Session
    {
        public uint SessionId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string RoomId { get; set; } = string.Empty;
        public DateTimeOffset ConnectedAt { get; set; }
        public bool IsMuted { get; set; }
        public double VolumeDb { get; set; } = 0;
        public double LastRms { get; set; }
        public int LatencyMs { get; set; }
        public DateTimeOffset LastAudioAt { get; set; }
        public DateTimeOffset LastControlAt { get; set; }
        public DateTimeOffset LastActivityAt { get; set; }
        public DateTimeOffset ControlWindowStart { get; set; }
        public int ControlMessagesThisWindow { get; set; }
        public DateTimeOffset AudioWindowStart { get; set; }
        public int AudioMessagesThisWindow { get; set; }
        public DateTimeOffset LastPingSentAt { get; set; }
    }

    private sealed class NetPeerComparer : IEqualityComparer<NetPeer>
    {
        public static NetPeerComparer Instance { get; } = new();
        public bool Equals(NetPeer? x, NetPeer? y) => ReferenceEquals(x, y);
        public int GetHashCode(NetPeer obj) => obj.Id.GetHashCode();
    }
}
