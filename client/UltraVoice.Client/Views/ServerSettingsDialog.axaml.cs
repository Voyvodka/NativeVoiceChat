using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UltraVoice.Shared.Configuration;

namespace UltraVoice.Client.Views;

public partial class ServerSettingsDialog : Window
{
    public ServerSettingsDialog()
    {
        InitializeComponent();
    }

    public ServerSettingsDialog(string host, int port, string? token) : this()
    {
        HostBox.Text = host;
        PortBox.Text = port.ToString();
        TokenBox.Text = token ?? string.Empty;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var host = HostBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            return;
        }

        var token = string.IsNullOrWhiteSpace(TokenBox.Text) ? null : TokenBox.Text?.Trim();
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
