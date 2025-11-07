using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
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
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;
    private uint _sessionId;

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
        if (_netManager.IsRunning)
        {
            return Task.CompletedTask;
        }

        if (!_netManager.Start())
        {
            throw new InvalidOperationException("Failed to start client NetManager");
        }

        _cts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);

        _state.SetConnectionStatus("Connecting...");
        _serverPeer = _netManager.Connect(_config.Server.Host, _config.Server.Port, string.Empty);

        return Task.CompletedTask;
    }

    public async Task JoinRoomAsync(string roomId)
    {
        if (string.IsNullOrWhiteSpace(_config.Username))
        {
            throw new InvalidOperationException("Username must be set before joining a room.");
        }

        await ConnectAsync();

        if (_serverPeer is null)
        {
            throw new InvalidOperationException("Server peer not connected.");
        }

        var hello = new HelloMessage
        {
            Username = _config.Username,
            RoomId = roomId,
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

    void INetEventListener.OnConnectionRequest(ConnectionRequest request)
    {
        request.Reject();
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        _state.SetConnectionStatus($"Error: {socketError}");
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // TODO: feed into telemetry
    }

    void INetEventListener.OnPeerConnected(NetPeer peer)
    {
        _state.SetConnectionStatus("Connected - awaiting welcome");
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _state.SetConnectionStatus($"Disconnected: {disconnectInfo.Reason}");
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
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
                _state.SetConnectionStatus($"Connected as {_config.Username}");
                break;
            case MessageType.State:
                var state = MessagePackSerializer.Deserialize<StateMessage>(envelope.Payload);
                _state.UpdateState(state);
                break;
            case MessageType.AudioFrame:
                var frame = MessagePackSerializer.Deserialize<AudioFrameMessage>(envelope.Payload);
                _audioSink?.HandleAudio(envelope.SessionId, frame);
                break;
            case MessageType.Pong:
                break;
            default:
                break;
        }
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

    private async Task PumpAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            _netManager.PollEvents();
            await Task.Delay(10, token).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _pumpTask?.GetAwaiter().GetResult();
        _netManager.Stop();
    }

    private static uint CurrentTimestamp() => (uint)Environment.TickCount64;
}

public interface IAudioSink
{
    void HandleAudio(uint sessionId, AudioFrameMessage frame);
}
