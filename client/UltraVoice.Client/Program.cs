using Avalonia;
using Avalonia.ReactiveUI;

namespace UltraVoice.Client;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
#else
        const string MutexName = @"Global\UltraVoice.Client.Singleton";
        var createdNew = false;
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);

        if (!createdNew)
        {
            // TODO: Consider sending an IPC message to focus the running instance.
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
#endif
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .LogToTrace();
}
