using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace UltraVoice.Client.Views;

public partial class UsernameDialog : Window
{
    public UsernameDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var input = this.FindControl<TextBox>("InputBox");
        var value = input?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Close(value);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
