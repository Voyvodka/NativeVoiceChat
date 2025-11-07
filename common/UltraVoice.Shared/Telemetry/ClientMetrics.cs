namespace UltraVoice.Shared.Telemetry;

public sealed class ClientMetrics
{
    public double AvgCpuPercent { get; init; }
    public double RamMb { get; init; }
    public double RttMs { get; init; }
    public double JitterMs { get; init; }
    public double PacketLossPercent { get; init; }
    public int DecodeErrors { get; init; }
}

public sealed class ServerMetrics
{
    public IReadOnlyDictionary<string, int> RoomUserCount { get; init; } =
        new Dictionary<string, int>();

    public double ForwardHz { get; init; }
    public double AvgRtt { get; init; }
    public int Errors { get; init; }
}
