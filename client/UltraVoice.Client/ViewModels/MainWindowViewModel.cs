using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.ComponentModel;
using ReactiveUI;
using UltraVoice.Client.Services;

namespace UltraVoice.Client.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly AppState _state;
    private readonly ClientTransport _transport;
    private readonly AudioEngine _audio;
    private readonly ConfigStore _configStore;
    private readonly Dictionary<uint, UserViewModel> _userCache = new();
    private readonly Subject<(UserViewModel vm, UserChangeKind kind)> _userChanges = new();
    private readonly CompositeDisposable _cleanup = new();
    private static readonly TimeSpan VolumeDebounceInterval = TimeSpan.FromMilliseconds(200);

    [ObservableProperty]
    private ObservableCollection<RoomViewModel> rooms = [];

    [ObservableProperty]
    private ObservableCollection<UserViewModel> users = [];

    [ObservableProperty]
    private string connectionStatus = "Disconnected";

    [ObservableProperty]
    private string telemetrySummary = string.Empty;

    [ObservableProperty]
    private string footerStatus = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AudioDeviceOption> inputDevices = [];

    [ObservableProperty]
    private IReadOnlyList<AudioDeviceOption> outputDevices = [];

    [ObservableProperty]
    private AudioDeviceOption? selectedInputDevice;

    [ObservableProperty]
    private AudioDeviceOption? selectedOutputDevice;

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

        var muteSubscription = _userChanges
            .Where(change => change.kind == UserChangeKind.Mute)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(change => SendUserChange(change.vm));
        _cleanup.Add(muteSubscription);

        var volumeSubscription = _userChanges
            .Where(change => change.kind == UserChangeKind.Volume)
            .GroupBy(change => change.vm.SessionId)
            .Subscribe(group =>
            {
                var throttled = group
                    .Throttle(VolumeDebounceInterval)
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(change => SendUserChange(change.vm));
                _cleanup.Add(throttled);
            });
        _cleanup.Add(volumeSubscription);

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

        SelectedInputDevice = FindDevice(InputDevices, _state.Configuration.InputDeviceId)
            ?? InputDevices.FirstOrDefault();

        SelectedOutputDevice = FindDevice(OutputDevices, _state.Configuration.OutputDeviceId)
            ?? OutputDevices.FirstOrDefault();

        FooterStatus = $"Ready — target server {_state.Configuration.Server.Host}:{_state.Configuration.Server.Port}";
    }

    public async Task SetUsernameAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        _state.Configuration.Username = _state.NormalizeUsername(username);
        await ConfigStore.SaveAsync(_state.Configuration);
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
            FooterStatus = "LÃ¼tfen Ã¶nce kullanÄ±cÄ± adÄ±nÄ±zÄ± girin.";
            return;
        }

        FooterStatus = $"Joining {roomId}...";

        try
        {
            await _transport.JoinRoomAsync(roomId);
            _state.SetCurrentRoom(roomId);
            _ = ConfigStore.SaveAsync(_state.Configuration);
            _audio.StartCapture();
            FooterStatus = $"Joined {roomId}";
        }
        catch (Exception ex)
        {
            FooterStatus = $"BaÄŸlantÄ± hatasÄ±: {ex.Message}";
        }
    }

    partial void OnSelectedInputDeviceChanged(AudioDeviceOption? value)
    {
        var id = value?.Id;
        _audio.SelectInputDevice(id);
        _state.Configuration.InputDeviceId = id;
        _ = ConfigStore.SaveAsync(_state.Configuration);
    }

    partial void OnSelectedOutputDeviceChanged(AudioDeviceOption? value)
    {
        var id = value?.Id;
        _audio.SelectOutputDevice(id);
        _state.Configuration.OutputDeviceId = id;
        _ = ConfigStore.SaveAsync(_state.Configuration);
    }

    partial void OnInputGainDbChanged(double value)
    {
        _audio.SetInputGain(value);
        _state.Configuration.InputGainDb = value;
        _ = ConfigStore.SaveAsync(_state.Configuration);
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
        Users ??= new ObservableCollection<UserViewModel>();

        var desiredOrder = new List<UserViewModel>(snapshot.Count);

        foreach (var user in snapshot)
        {
            if (!_userCache.TryGetValue(user.SessionId, out var viewModel))
            {
                viewModel = new UserViewModel(user, OnUserChanged);
                _userCache[user.SessionId] = viewModel;
                Users.Add(viewModel);
            }
            else
            {
                viewModel.ApplySnapshot(user);
            }

            desiredOrder.Add(viewModel);
        }

        for (var index = 0; index < desiredOrder.Count; index++)
        {
            var viewModel = desiredOrder[index];
            var currentIndex = Users.IndexOf(viewModel);

            if (currentIndex == index)
            {
                continue;
            }

            if (currentIndex >= 0)
            {
                Users.Move(currentIndex, index);
            }
            else
            {
                Users.Insert(index, viewModel);
            }
        }

        while (Users.Count > desiredOrder.Count)
        {
            var lastIndex = Users.Count - 1;
            var viewModel = Users[lastIndex];
            Users.RemoveAt(lastIndex);
            _userCache.Remove(viewModel.SessionId);
        }
    }

    private static AudioDeviceOption? FindDevice(IReadOnlyList<AudioDeviceOption> devices, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return devices.FirstOrDefault(device => string.Equals(device.Id, id, StringComparison.Ordinal));
    }

    private void OnUserChanged(UserViewModel vm, UserChangeKind kind) =>
        _userChanges.OnNext((vm, kind));

    private void SendUserChange(UserViewModel vm)
    {
        System.Diagnostics.Debug.WriteLine($"[UserChanged] Sending mute={vm.IsMuted} volume={vm.VolumeDb:F1} for {vm.Username}");
        _transport.SendUserEvent(vm.IsMuted, vm.VolumeDb);
    }

    public void Dispose()
    {
        _cleanup.Dispose();
        _userChanges.Dispose();
    }
}

