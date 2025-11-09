using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using MessagePack;
using UltraVoice.Shared.Audio;
using UltraVoice.Shared.Configuration;
using UltraVoice.Shared.Wire;

namespace UltraVoice.Client.Services;

public sealed class ClientTransport : INetEventListener, IDisposable
{
    private readonly AppState _state;
    private readonly ClientConfig _config;
    private readonly NetManager _netManager;
    private IAudioSink? _audioSink;
    private NetPeer? _serverPeer;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private string? _desiredRoomId;
    private uint _sessionId;
    private uint _lastPingSentMs;
    private double _lastRttMs;
    private static readonly TimeSpan ReconnectIdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    public ClientTransport(AppState state, ClientConfig config)
    {
        _state = state;
        _config = config;
        _netManager = new NetManager(this)
        {
            IPv6Enabled = false,
            UnconnectedMessagesEnabled = false,
            NatPunchEnabled = false
        };
    }

    public Task ConnectAsync()
    {
        EnsurePumpStarted();
        BeginConnect();
        return Task.CompletedTask;
    }

    private void EnsurePumpStarted()
    {
        if (_netManager.IsRunning)
        {
            return;
        }

        if (!_netManager.Start())
        {
            throw new InvalidOperationException("Failed to start client NetManager");
        }

        _pumpCts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => PumpAsync(_pumpCts.Token), CancellationToken.None);
    }

    private void BeginConnect()
    {
        if (_serverPeer is { ConnectionState: not ConnectionState.Disconnected })
        {
            return;
        }

        _state.SetConnectionStatus("Bağlanılıyor...");
        UpdateTelemetry(null);
        _serverPeer = _netManager.Connect(_config.Server.Host, _config.Server.Port, string.Empty);
    }

    public async Task JoinRoomAsync(string roomId)
    {
        if (string.IsNullOrWhiteSpace(_config.Username))
        {
            throw new InvalidOperationException("Username must be set before joining a room.");
        }

        _desiredRoomId = roomId;
        await ConnectAsync();

        TrySendHello();
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        request.Reject();
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        _state.SetConnectionStatus($"Bağlanılamadı: {socketError}");
        UpdateTelemetry(null);
        StartReconnectLoop();
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // TODO: feed into telemetry
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        _serverPeer = peer;
        _state.SetConnectionStatus("Bağlandı");
        StopReconnectLoop();
        TrySendHello();
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (ReferenceEquals(_serverPeer, peer))
        {
            _serverPeer = null;
        }

        _sessionId = 0;
        _state.SetConnectionStatus($"Bağlanılamadı: {disconnectInfo.Reason}");
        UpdateTelemetry(null);
        StartReconnectLoop();
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        try
        {
            var envelope = MessagePackSerializer.Deserialize<MessageEnvelope>(reader.GetRemainingBytes());
            HandleEnvelope(envelope);
        }
        catch
        {
            // Ignore malformed packets for MVP
        }
        finally
        {
            reader.Recycle();
        }
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        reader.Recycle();
    }

    private void HandleEnvelope(MessageEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case MessageType.Welcome:
                var welcome = MessagePackSerializer.Deserialize<WelcomeMessage>(envelope.Payload);
                _sessionId = welcome.SessionId;
                _state.SetConnectionStatus($"Bağlandı ({_config.Username})");
                StopReconnectLoop();
                break;
            case MessageType.State:
                var state = MessagePackSerializer.Deserialize<StateMessage>(envelope.Payload);
                _state.UpdateState(state);
                break;
            case MessageType.AudioFrame:
                var frame = MessagePackSerializer.Deserialize<AudioFrameMessage>(envelope.Payload);
                _audioSink?.HandleAudio(envelope.SessionId, frame);
                break;
            case MessageType.Ping:
                HandlePing(envelope.Payload);
                break;
            case MessageType.Pong:
                HandlePong(envelope.Payload);
                break;
            default:
                break;
        }
    }

    private void TrySendHello()
    {
        if (_serverPeer is null || _serverPeer.ConnectionState != ConnectionState.Connected)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_desiredRoomId))
        {
            return;
        }

        var hello = new HelloMessage
        {
            Username = _config.Username,
            RoomId = _desiredRoomId,
            Token = _config.Server.Token
        };

        var envelope = new MessageEnvelope
        {
            Type = MessageType.Hello,
            SessionId = _sessionId,
            Sequence = 0,
            TimestampMs = CurrentTimestamp(),
            Payload = MessagePackSerializer.Serialize(hello)
        };

        _serverPeer.Send(MessagePackSerializer.Serialize(envelope), DeliveryMethod.ReliableOrdered);
    }

    private void HandlePing(byte[] payload)
    {
        if (_serverPeer is null || _sessionId == 0)
        {
            return;
        }

        var envelope = new MessageEnvelope
        {
            Type = MessageType.Pong,
            SessionId = _sessionId,
            Sequence = 0,
            TimestampMs = CurrentTimestamp(),
            Payload = payload
        };

        _serverPeer.Send(MessagePackSerializer.Serialize(envelope), DeliveryMethod.ReliableOrdered);
    }

    private void HandlePong(byte[] payload)
    {
        if (payload is null || payload.Length < 4)
        {
            return;
        }

        var sentTs = BitConverter.ToUInt32(payload, 0);
        var now = CurrentTimestamp();
        var rtt = (uint)(now - sentTs); // handles wrap naturally for uint
        _lastRttMs = rtt;
        UpdateTelemetry(rtt);
    }

    public void SendAudioFrame(AudioFrameMessage frame)
    {
        if (_serverPeer is null || _sessionId == 0)
        {
            return;
        }

        var envelope = new MessageEnvelope
        {
            Type = MessageType.AudioFrame,
            SessionId = _sessionId,
            Sequence = frame.Sequence,
            TimestampMs = frame.CaptureTimestampMs,
            Payload = MessagePackSerializer.Serialize(frame)
        };

        _serverPeer.Send(MessagePackSerializer.Serialize(envelope), DeliveryMethod.Sequenced);
    }

    public void RegisterAudioSink(IAudioSink sink)
    {
        _audioSink = sink;
    }

    public void SendUserEvent(bool? mute, double? volumeDb)
    {
        if (_serverPeer is null || _sessionId == 0)
        {
            return;
        }

        var evt = new UserEventMessage
        {
            Mute = mute,
            VolumeDb = volumeDb
        };

        var envelope = new MessageEnvelope
        {
            Type = MessageType.UserEvent,
            SessionId = _sessionId,
            Sequence = 0,
            TimestampMs = CurrentTimestamp(),
            Payload = MessagePackSerializer.Serialize(evt)
        };

        _serverPeer.Send(MessagePackSerializer.Serialize(envelope), DeliveryMethod.ReliableOrdered);
    }

    private async Task PumpAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            _netManager.PollEvents();
            TrySendPing();
            await Task.Delay(10, token).ConfigureAwait(false);
        }
    }

    private void TrySendPing()
    {
        if (_serverPeer is null || _sessionId == 0)
        {
            return;
        }

        var now = CurrentTimestamp();
        if (now - _lastPingSentMs < 1000)
        {
            return;
        }

        _lastPingSentMs = now;
        var payload = BitConverter.GetBytes(now);
        var envelope = new MessageEnvelope
        {
            Type = MessageType.Ping,
            SessionId = _sessionId,
            Sequence = 0,
            TimestampMs = now,
            Payload = payload
        };

        _serverPeer.Send(MessagePackSerializer.Serialize(envelope), DeliveryMethod.ReliableOrdered);
    }

    public void Disconnect()
    {
        StopReconnectLoop();

        if (_serverPeer is { })
        {
            try
            {
                _serverPeer.Disconnect();
            }
            catch
            {
                // ignored
            }
        }

        _serverPeer = null;
        _sessionId = 0;
    }

    private void StartReconnectLoop()
    {
        if (string.IsNullOrWhiteSpace(_desiredRoomId))
        {
            return;
        }

        if (_reconnectTask is { IsCompleted: false })
        {
            return;
        }

        _reconnectCts = new CancellationTokenSource();
        _reconnectTask = Task.Run(() => ReconnectLoopAsync(_reconnectCts.Token), CancellationToken.None);
    }

    private void StopReconnectLoop()
    {
        if (_reconnectCts is null)
        {
            return;
        }

        _reconnectCts.Cancel();
        _reconnectCts = null;
        _reconnectTask = null;
    }

    private async Task ReconnectLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var isConnected = _serverPeer is { ConnectionState: not ConnectionState.Disconnected };
            if (isConnected || string.IsNullOrWhiteSpace(_desiredRoomId))
            {
                try
                {
                    await Task.Delay(ReconnectIdleDelay, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                continue;
            }

            try
            {
                await ConnectAsync().ConfigureAwait(false);
            }
            catch
            {
                _state.SetConnectionStatus("Bağlanılamadı");
            }

            try
            {
                await Task.Delay(ReconnectDelay, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _reconnectCts?.Cancel();
        _pumpCts?.Cancel();
        try
        {
            _reconnectTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            _pumpTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _netManager.Stop();
    }

    private static uint CurrentTimestamp() => (uint)Environment.TickCount64;

    private void UpdateTelemetry(uint? rttMs)
    {
        var host = _config.Server.Host;
        var port = _config.Server.Port;
        var baseText = $"Server {host}:{port}";
        var text = rttMs.HasValue ? $"{baseText} | RTT {rttMs.Value} ms" : baseText;
        _state.SetTelemetry(text);
    }
}

public interface IAudioSink
{
    void HandleAudio(uint sessionId, AudioFrameMessage frame);
}
