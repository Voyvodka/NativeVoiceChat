using MessagePack;

namespace UltraVoice.Shared.Wire;

/// <summary>
/// Snapshot of room membership broadcast periodically by the server.
/// </summary>
[MessagePackObject]
public sealed class RoomState
{
    [Key(0)]
    public string RoomId { get; init; } = string.Empty;

    [Key(1)]
    public IReadOnlyList<UserSummary> Users { get; init; } = Array.Empty<UserSummary>();
}

/// <summary>
/// Lightweight description for users listed in presence updates.
/// </summary>
[MessagePackObject]
public sealed class UserSummary
{
    [Key(0)]
    public uint SessionId { get; init; }

    [Key(1)]
    public string Username { get; init; } = string.Empty;

    [Key(2)]
    public ulong JoinedAtUnixMs { get; init; }

    [Key(3)]
    public bool IsMuted { get; init; }

    [Key(4)]
    public double VolumeDb { get; init; }
}
