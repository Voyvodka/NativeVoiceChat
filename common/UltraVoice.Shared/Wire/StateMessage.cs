using MessagePack;

namespace UltraVoice.Shared.Wire;

[MessagePackObject]
public sealed class StateMessage
{
    [Key(0)]
    public IReadOnlyList<RoomState> Rooms { get; init; } = Array.Empty<RoomState>();

    [Key(1)]
    public IReadOnlyList<uint> ActiveSpeakers { get; init; } = Array.Empty<uint>();
}
