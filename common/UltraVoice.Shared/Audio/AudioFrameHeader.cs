namespace UltraVoice.Shared.Audio;

/// <summary>
/// Descriptor that accompanies Opus payloads forwarded by the server.
/// </summary>
public readonly record struct AudioFrameHeader(
    uint SessionId,
    uint TimestampMs,
    byte Sequence,
    ushort PayloadLength);
