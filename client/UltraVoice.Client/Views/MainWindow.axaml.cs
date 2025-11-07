using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UltraVoice.Client.ViewModels;

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
