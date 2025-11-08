namespace UltraVoice.Client.ViewModels;

public sealed class RoomViewModel
{
    public string RoomId { get; }
    public int UserCount { get; }
    public string PingText { get; }

    public RoomViewModel(string roomId, int userCount, string pingText)
    {
        RoomId = roomId;
        UserCount = userCount;
        PingText = pingText;
    }
}
