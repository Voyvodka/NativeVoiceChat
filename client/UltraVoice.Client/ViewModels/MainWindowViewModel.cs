using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using ReactiveUI;
using UltraVoice.Client.Services;

namespace UltraVoice.Client.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly ClientTransport _transport;
    private readonly AudioEngine _audio;
    private readonly ConfigStore _configStore;

    [ObservableProperty]
    private ObservableCollection<RoomViewModel> rooms = new();

    [ObservableProperty]
    private ObservableCollection<UserViewModel> users = new();

    [ObservableProperty]
    private string connectionStatus = "Disconnected";

    [ObservableProperty]
    private string telemetrySummary = string.Empty;

    [ObservableProperty]
    private string footerStatus = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> inputDevices = Array.Empty<string>();

    [ObservableProperty]
    private IReadOnlyList<string> outputDevices = Array.Empty<string>();

    [ObservableProperty]
    private string? selectedInputDevice;

    [ObservableProperty]
    private string? selectedOutputDevice;

    [ObservableProperty]
    private double inputGainDb;

    public ReactiveCommand<string, Unit> JoinRoomCommand { get; }
    public string Username => _state.Configuration.Username;
    public string CurrentRoom => _state.CurrentRoomValue;

    public MainWindowViewModel(
        AppState state,
        ClientTransport transport,
        AudioEngine audio,
        ConfigStore configStore)
    {
        _state = state;
        _transport = transport;
        _audio = audio;
        _configStore = configStore;

        JoinRoomCommand = ReactiveCommand.CreateFromTask<string>(JoinRoomAsync);

        inputGainDb = _state.Configuration.InputGainDb;
        selectedInputDevice = _state.Configuration.InputDeviceId;
        selectedOutputDevice = _state.Configuration.OutputDeviceId;

        _state.Rooms
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateRooms);

        _state.Users
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateUsers);

        _state.ConnectionStatus
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(status => ConnectionStatus = status);

        _state.Telemetry
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(metrics => TelemetrySummary = metrics);

        InputDevices = _audio.GetInputDevices();
        OutputDevices = _audio.GetOutputDevices();

        if (InputDevices.Count > 0 && string.IsNullOrWhiteSpace(SelectedInputDevice))
        {
            SelectedInputDevice = InputDevices[0];
        }

        if (OutputDevices.Count > 0 && string.IsNullOrWhiteSpace(SelectedOutputDevice))
        {
            SelectedOutputDevice = OutputDevices[0];
        }

        FooterStatus = $"Ready – target server {_state.Configuration.Server.Host}:{_state.Configuration.Server.Port}";
    }

    public async Task SetUsernameAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        _state.Configuration.Username = username.Trim();
        await _configStore.SaveAsync(_state.Configuration);
        OnPropertyChanged(nameof(Username));
    }

    public async Task JoinRoomAsync(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_state.Configuration.Username))
        {
            FooterStatus = "Lütfen önce kullanıcı adınızı girin.";
            return;
        }

        FooterStatus = $"Joining {roomId}...";

        try
        {
            await _transport.JoinRoomAsync(roomId);
            _state.SetCurrentRoom(roomId);
            _ = _configStore.SaveAsync(_state.Configuration);
            _audio.StartCapture();
            FooterStatus = $"Joined {roomId}";
        }
        catch (Exception ex)
        {
            FooterStatus = $"Bağlantı hatası: {ex.Message}";
        }
    }

    partial void OnSelectedInputDeviceChanged(string? value)
    {
        _audio.SelectInputDevice(value);
        _state.Configuration.InputDeviceId = value;
        _ = _configStore.SaveAsync(_state.Configuration);
    }

    partial void OnSelectedOutputDeviceChanged(string? value)
    {
        _audio.SelectOutputDevice(value);
        _state.Configuration.OutputDeviceId = value;
        _ = _configStore.SaveAsync(_state.Configuration);
    }

    partial void OnInputGainDbChanged(double value)
    {
        _audio.SetInputGain(value);
        _state.Configuration.InputGainDb = value;
        _ = _configStore.SaveAsync(_state.Configuration);
    }

    private void UpdateRooms(IReadOnlyCollection<RoomSnapshot> snapshot)
    {
        Rooms = new ObservableCollection<RoomViewModel>(
            snapshot.Select(room => new RoomViewModel(
                room.RoomId,
                room.UserCount,
                $"{room.PingMs} ms")));
    }

    private void UpdateUsers(IReadOnlyCollection<UserSnapshot> snapshot)
    {
        Users = new ObservableCollection<UserViewModel>(
            snapshot.Select(user => new UserViewModel(user)));
    }
}

public sealed class RoomViewModel
{
    public string RoomId { get; }
    public int UserCount { get; }
    public string PingText { get; }

    public RoomViewModel(string roomId, int userCount, string pingText)
    {
        RoomId = roomId;
        UserCount = userCount;
        PingText = pingText;
    }
}

public sealed partial class UserViewModel : ObservableObject
{
    public UserViewModel(UserSnapshot snapshot)
    {
        Username = snapshot.Username;
        IsMuted = snapshot.IsMuted;
        VolumeDb = snapshot.VolumeDb;
        Level = snapshot.Level;
        ActivityBrush = snapshot.ActivityBrush;
    }

    [ObservableProperty]
    private string username;

    [ObservableProperty]
    private bool isMuted;

    [ObservableProperty]
    private double volumeDb;

    [ObservableProperty]
    private double level;

    [ObservableProperty]
    private IBrush activityBrush;
}
