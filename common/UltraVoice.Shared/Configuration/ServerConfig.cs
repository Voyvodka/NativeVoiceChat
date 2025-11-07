using System.Text.Json.Serialization;

namespace UltraVoice.Shared.Configuration;

/// <summary>
/// Simple JSON-backed server configuration for MVP deployments.
/// </summary>
public sealed class ServerConfig
{
    [JsonPropertyName("port")]
    public int Port { get; set; } = 40000;

    [JsonPropertyName("rooms")]
    public string[] Rooms { get; set; } = ["room-a", "room-b", "room-c"];

    [JsonPropertyName("maxUsersPerRoom")]
    public int MaxUsersPerRoom { get; set; } = 16;

    [JsonPropertyName("sharedToken")]
    public string? SharedToken { get; set; }

    public static ServerConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ServerConfig();
        }

        using var stream = File.OpenRead(path);
        return System.Text.Json.JsonSerializer.Deserialize<ServerConfig>(stream) ?? new ServerConfig();
    }
}
