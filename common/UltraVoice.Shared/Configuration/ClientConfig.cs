using System.Text.Json.Serialization;

namespace UltraVoice.Shared.Configuration;

/// <summary>
/// Persistent configuration stored under %AppData%/UltraVoice/config.json as defined in the PRD.
/// </summary>
public sealed class ClientConfig
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("lastRoom")]
    public string LastRoom { get; set; } = "room-a";

    [JsonPropertyName("inputGainDb")]
    public double InputGainDb { get; set; } = 0;

    [JsonPropertyName("perUserVolumeDb")]
    public Dictionary<string, double> PerUserVolumeDb { get; set; } = new();

    [JsonPropertyName("inputDeviceId")]
    public string? InputDeviceId { get; set; }

    [JsonPropertyName("outputDeviceId")]
    public string? OutputDeviceId { get; set; }

    [JsonPropertyName("server")]
    public ServerEndpoint Server { get; set; } = new();

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UltraVoice",
            "config.json");
}

public sealed class ServerEndpoint
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 40000;

    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
