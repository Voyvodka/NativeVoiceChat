using MessagePack;
using UltraVoice.Shared.Configuration;

namespace UltraVoice.Shared.Wire;

[MessagePackObject]
public sealed class HelloMessage
{
    [Key(0)]
    public string Username { get; init; } = string.Empty;

    [Key(1)]
    public string RoomId { get; init; } = RoomCatalog.DefaultRoom;

    [Key(2)]
    public string? Token { get; init; }
}

[MessagePackObject]
public sealed class WelcomeMessage
{
    [Key(0)]
    public uint SessionId { get; init; }

    [Key(1)]
    public IReadOnlyList<string> AvailableRooms { get; init; } = Array.Empty<string>();
}

[MessagePackObject]
public sealed class UserEventMessage
{
    [Key(0)]
    public bool? Mute { get; init; }

    [Key(1)]
    public double? VolumeDb { get; init; }
}
