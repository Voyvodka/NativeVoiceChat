using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UltraVoice.Server.Hosting;
using UltraVoice.Server.Networking;
using UltraVoice.Shared.Configuration;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole();

var configPath = args.Length > 0 ? args[0] : "server.json";
builder.Services.AddSingleton(ServerConfig.Load(configPath));
builder.Services.AddSingleton<SfuServer>();
builder.Services.AddHostedService<SfuHostedService>();

await builder.Build().RunAsync();
