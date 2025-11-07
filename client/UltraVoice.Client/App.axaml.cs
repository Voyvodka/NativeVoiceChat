using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using UltraVoice.Client.Services;
using UltraVoice.Client.ViewModels;
using UltraVoice.Client.Views;
using UltraVoice.Shared.Configuration;

namespace UltraVoice.Client;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _serviceProvider = ConfigureServices();

            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ConfigStore>();
        services.AddSingleton<ClientConfig>(provider => provider.GetRequiredService<ConfigStore>().Load());
        services.AddSingleton<AppState>();
        services.AddSingleton<ClientTransport>();
        services.AddSingleton<AudioEngine>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton(provider => new MainWindow
        {
            DataContext = provider.GetRequiredService<MainWindowViewModel>()
        });

        return services.BuildServiceProvider();
    }
}
