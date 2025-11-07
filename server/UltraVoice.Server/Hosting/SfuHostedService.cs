using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UltraVoice.Server.Networking;

namespace UltraVoice.Server.Hosting;

/// <summary>
/// Hosted service wrapper that manages the lifetime of the SFU server.
/// </summary>
public sealed class SfuHostedService : IHostedService
{
    private readonly SfuServer _server;
    private readonly ILogger<SfuHostedService> _logger;

    public SfuHostedService(SfuServer server, ILogger<SfuHostedService> logger)
    {
        _server = server;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("UltraVoice SFU starting...");
        return _server.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("UltraVoice SFU stopping...");
        await _server.StopAsync(cancellationToken);
    }
}
