using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UltraVoice.Shared.Configuration;

namespace UltraVoice.Client.Views;

public partial class ServerSettingsDialog : Window
{
    private TextBox HostBoxControl => this.FindControl<TextBox>("HostBox") ?? throw new InvalidOperationException("HostBox not found in dialog XAML.");
    private TextBox PortBoxControl => this.FindControl<TextBox>("PortBox") ?? throw new InvalidOperationException("PortBox not found in dialog XAML.");
    private TextBox TokenBoxControl => this.FindControl<TextBox>("TokenBox") ?? throw new InvalidOperationException("TokenBox not found in dialog XAML.");

    public ServerSettingsDialog()
    {
        InitializeComponent();
    }

    public ServerSettingsDialog(string host, int port, string? token) : this()
    {
        HostBoxControl.Text = host;
        PortBoxControl.Text = port.ToString();
        TokenBoxControl.Text = token ?? string.Empty;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var host = HostBoxControl.Text?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        if (!int.TryParse(PortBoxControl.Text, out var port) || port is < 1 or > 65535)
        {
            return;
        }

        var token = string.IsNullOrWhiteSpace(TokenBoxControl.Text) ? null : TokenBoxControl.Text?.Trim();
        var endpoint = new ServerEndpoint
        {
            Host = host,
            Port = port,
            Token = token
        };

        Close(endpoint);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
