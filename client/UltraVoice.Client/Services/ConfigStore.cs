using System.Text.Json;
using UltraVoice.Shared.Configuration;

namespace UltraVoice.Client.Services;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public ClientConfig Load()
    {
        try
        {
            var path = ClientConfig.DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (!File.Exists(path))
            {
                var config = new ClientConfig();
                File.WriteAllText(path, JsonSerializer.Serialize(config, SerializerOptions));
                return config;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ClientConfig>(json, SerializerOptions) ?? new ClientConfig();
        }
        catch
        {
            return new ClientConfig();
        }
    }

    public async Task SaveAsync(ClientConfig config)
    {
        try
        {
            var path = ClientConfig.DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, SerializerOptions));
        }
        catch
        {
            // Fail silently for MVP; consider surfacing to UI later.
        }
    }
}
