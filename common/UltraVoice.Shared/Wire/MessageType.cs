namespace UltraVoice.Shared.Wire;

/// <summary>
/// Message types exchanged between UltraVoice clients and the SFU server.
/// Mirrors the wire protocol enumerations described in the PRD.
/// </summary>
public enum MessageType : byte
{
    Hello = 0x01,
    Welcome = 0x02,
    State = 0x03,
    AudioFrame = 0x04,
    Ping = 0x05,
    Pong = 0x06,
    UserEvent = 0x07,
    Goodbye = 0x08
}
