using System;
using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using UltraVoice.Client.Services;

namespace UltraVoice.Client.ViewModels;

public sealed partial class UserViewModel : ObservableObject
{
    private readonly Action<UserViewModel, UserChangeKind>? _onChanged;
    private bool _suppressNotifications;

    public UserViewModel(UserSnapshot snapshot, Action<UserViewModel, UserChangeKind>? onChanged = null)
    {
        _onChanged = onChanged;
        SessionId = snapshot.SessionId;
        ApplySnapshot(snapshot);
    }

    public uint SessionId { get; }

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private double volumeDb;

    [ObservableProperty]
    private double level;

    [ObservableProperty]
    private IBrush activityBrush = Brushes.Gray;

    public void ApplySnapshot(UserSnapshot snapshot)
    {
        _suppressNotifications = true;
        Username = snapshot.Username;
        IsMuted = snapshot.IsMuted;
        VolumeDb = snapshot.VolumeDb;
        Level = snapshot.Level;
        ActivityBrush = snapshot.ActivityBrush;
        _suppressNotifications = false;
    }

    partial void OnIsMutedChanged(bool value)
    {
        Debug.WriteLine($"[MuteToggle] {Username} -> {(value ? "Muted" : "Live")} (suppress={_suppressNotifications})");
        if (_suppressNotifications)
        {
            return;
        }

        _onChanged?.Invoke(this, UserChangeKind.Mute);
    }

    partial void OnVolumeDbChanged(double value)
    {
        Debug.WriteLine($"[VolumeSlider] {Username} -> {value:F1} dB (suppress={_suppressNotifications})");
        if (_suppressNotifications)
        {
            return;
        }

        _onChanged?.Invoke(this, UserChangeKind.Volume);
    }
}

public enum UserChangeKind
{
    Volume,
    Mute,
}
