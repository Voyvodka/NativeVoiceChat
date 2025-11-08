using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Media;
using UltraVoice.Shared.Configuration;
using UltraVoice.Shared.Wire;

namespace UltraVoice.Client.Services;

public sealed class AppState
{
    private readonly BehaviorSubject<IReadOnlyCollection<RoomSnapshot>> _rooms =
        new(Array.Empty<RoomSnapshot>());

    private readonly BehaviorSubject<IReadOnlyCollection<UserSnapshot>> _users =
        new(Array.Empty<UserSnapshot>());

    private readonly BehaviorSubject<string> _connectionStatus =
        new("Disconnected");

    private readonly BehaviorSubject<string> _telemetry =
        new(string.Empty);

    private readonly BehaviorSubject<IReadOnlyCollection<uint>> _activeSpeakers =
        new(Array.Empty<uint>());

    private readonly BehaviorSubject<string> _currentRoom;

    private StateMessage? _latestState;
    private readonly object _gate = new();
    private readonly Dictionary<uint, double> _userLevels = new();
    private IReadOnlyCollection<uint> _activeSpeakersCurrent = Array.Empty<uint>();

    public ClientConfig Configuration { get; }

    public IObservable<IReadOnlyCollection<RoomSnapshot>> Rooms => _rooms.AsObservable();
    public IObservable<IReadOnlyCollection<UserSnapshot>> Users => _users.AsObservable();
    public IObservable<string> ConnectionStatus => _connectionStatus.AsObservable();
    public IObservable<string> Telemetry => _telemetry.AsObservable();
    public IObservable<IReadOnlyCollection<uint>> ActiveSpeakers => _activeSpeakers.AsObservable();
    public IObservable<string> CurrentRoom => _currentRoom.AsObservable();
    public string CurrentRoomValue => _currentRoom.Value;

    public AppState(ClientConfig config)
    {
        Configuration = config;
        Configuration.Username = NormalizeUsername(Configuration.Username);
        var fallbackRoom = RoomCatalog.Normalize(config.LastRoom);
        _currentRoom = new BehaviorSubject<string>(fallbackRoom);
    }

    public void SetConnectionStatus(string status) =>
        _connectionStatus.OnNext(status);

    public void SetTelemetry(string summary) =>
        _telemetry.OnNext(summary);

    public void UpdateState(StateMessage state)
    {
        lock (_gate)
        {
            _latestState = state;
        }

        var rooms = state.Rooms
            .Select(r => new RoomSnapshot(r.RoomId, r.Users.Count, PingMs: 0))
            .ToArray();
        _rooms.OnNext(rooms);
        _activeSpeakersCurrent = state.ActiveSpeakers;
        _activeSpeakers.OnNext(state.ActiveSpeakers);

        UpdateUsersForRoom(_currentRoom.Value, state);
    }

    public void SetCurrentRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return;
        }

        if (_currentRoom.Value == roomId)
        {
            return;
        }

        _currentRoom.OnNext(roomId);
        Configuration.LastRoom = roomId;
        UpdateUsersForRoom(roomId);
    }

    public void UpdateUserLevel(uint sessionId, double rms)
    {
        lock (_gate)
        {
            _userLevels[sessionId] = rms;
        }

        UpdateUsersForRoom(_currentRoom.Value);
    }

    private void UpdateUsersForRoom(string roomId, StateMessage? snapshotOverride = null)
    {
        StateMessage? state = snapshotOverride;
        if (state is null)
        {
            lock (_gate)
            {
                state = _latestState;
            }
        }

        if (state is null)
        {
            _users.OnNext(Array.Empty<UserSnapshot>());
            return;
        }

        var room = state.Rooms.FirstOrDefault(r => r.RoomId == roomId);
        if (room is null)
        {
            _users.OnNext(Array.Empty<UserSnapshot>());
            return;
        }

        var users = room.Users
            .Select(u => new UserSnapshot(
                u.SessionId,
                u.Username,
                IsMuted: u.IsMuted,
                VolumeDb: u.VolumeDb,
                Level: GetLevel(u.SessionId),
                ActivityBrush: GetActivityBrush(u.SessionId)))
            .ToArray();

        _users.OnNext(users);
    }

    private double GetLevel(uint sessionId)
    {
        lock (_gate)
        {
            return _userLevels.TryGetValue(sessionId, out var level) ? level : 0;
        }
    }

    private IBrush GetActivityBrush(uint sessionId)
    {
        var isActive = _activeSpeakersCurrent.Contains(sessionId);
        return isActive ? Brushes.LimeGreen : Brushes.Gray;
    }

    public string NormalizeUsername(string? raw)
    {
        var input = raw?.Trim() ?? string.Empty;
        var machine = Environment.MachineName;

        if (string.IsNullOrWhiteSpace(machine))
        {
            return input;
        }

        var suffix = $"@{machine}";

        if (string.IsNullOrWhiteSpace(input))
        {
            return suffix.TrimStart('@');
        }

        if (input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return input;
        }

        return $"{input}{suffix}";
    }
}

public sealed record RoomSnapshot(string RoomId, int UserCount, int PingMs);

public sealed record UserSnapshot(
    uint SessionId,
    string Username,
    bool IsMuted,
    double VolumeDb,
    double Level,
    IBrush ActivityBrush);
