using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UltraVoice.Client.ViewModels;
using UltraVoice.Shared.Configuration;

namespace UltraVoice.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ = EnsureUsernameAndJoinAsync();
    }

    private async void OnServerSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var snapshot = vm.GetServerEndpointSnapshot();
        var dialog = new ServerSettingsDialog(snapshot.Host, snapshot.Port, snapshot.Token);
        var result = await dialog.ShowDialog<ServerEndpoint?>(this);

        if (result is null)
        {
            return;
        }

        await vm.UpdateServerEndpointAsync(result);
    }

    private async Task EnsureUsernameAndJoinAsync()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        while (string.IsNullOrWhiteSpace(vm.Username))
        {
            var dialog = new UsernameDialog
            {
                Title = "Kullanıcı Adı"
            };

            var result = await dialog.ShowDialog<string?>(this);
            if (string.IsNullOrWhiteSpace(result))
            {
                continue;
            }

            await vm.SetUsernameAsync(result);
        }

        await vm.JoinRoomAsync(vm.CurrentRoom);
    }
}
