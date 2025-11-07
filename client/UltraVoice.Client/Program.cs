using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;

namespace UltraVoice.Client;

internal static class Program
{
    private const string MutexName = @"Global\UltraVoice.Client.Singleton";
    private static Mutex? _mutex;

    [STAThread]
    public static void Main(string[] args)
    {
        var createdNew = false;
        _mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);

        if (!createdNew)
        {
            // TODO: Consider sending an IPC message to focus the running instance.
            return;
        }

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .LogToTrace();
}
