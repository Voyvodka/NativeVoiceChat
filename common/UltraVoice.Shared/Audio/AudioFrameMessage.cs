using MessagePack;

namespace UltraVoice.Shared.Audio;

/// <summary>
/// Wire format for Opus encoded audio frames.
/// </summary>
[MessagePackObject]
public sealed class AudioFrameMessage
{
    /// <summary>
    /// RTP-like sequence number, wraps at 65535.
    /// </summary>
    [Key(0)]
    public ushort Sequence { get; init; }

    /// <summary>
    /// Capture timestamp in milliseconds since Environment.TickCount64.
    /// </summary>
    [Key(1)]
    public uint CaptureTimestampMs { get; init; }

    /// <summary>
    /// Root mean square level calculated client-side, used for active speaker selection.
    /// </summary>
    [Key(2)]
    public float Rms { get; init; }

    /// <summary>
    /// Encoded Opus payload.
    /// </summary>
    [Key(3)]
    public byte[] Payload { get; init; } = Array.Empty<byte>();
}
