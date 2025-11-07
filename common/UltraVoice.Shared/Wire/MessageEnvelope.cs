using MessagePack;

namespace UltraVoice.Shared.Wire;

/// <summary>
/// Common message header for all control plane packets.
/// </summary>
[MessagePackObject]
public sealed class MessageEnvelope
{
    [Key(0)]
    public MessageType Type { get; init; }

    [Key(1)]
    public uint SessionId { get; init; }

    [Key(2)]
    public ushort Sequence { get; init; }

    [Key(3)]
    public uint TimestampMs { get; init; }

    [Key(4)]
    public byte[] Payload { get; init; } = Array.Empty<byte>();
}
