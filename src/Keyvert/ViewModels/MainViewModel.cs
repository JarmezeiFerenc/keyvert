using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Keyvert.Core;
using Keyvert.Services;
using Microsoft.Win32;

namespace Keyvert.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public const string DriverDownloadUrl = "https://github.com/nefarius/ViGEmBus/releases/latest";

    private readonly EmulationEngine _engine;
    private readonly ProfileStore _profiles;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly AppLog _log;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<ControllerAction, BindingRowViewModel> _rows = [];
    private readonly DispatcherTimer _toastTimer;
    private readonly object _snapshotLock = new();
    private ControllerSnapshot _latestSnapshot;
    private bool _snapshotQueued;
    private ProfileData _profile = new();
    private int _toggleKey;
    private bool _syncing;
    private BindingRowViewModel? _captureRow;
    private bool _capturingToggleKey;
    private TaskCompletionSource<string?>? _prompt;
    private Func<string, string?>? _promptValidator;

    public MainViewModel(EmulationEngine engine, ProfileStore profiles, SettingsStore settingsStore, AppSettings settings, AppLog log)
    {
        _engine = engine;
        _profiles = profiles;
        _settingsStore = settingsStore;
        _settings = settings;
        _log = log;
        _dispatcher = Dispatcher.CurrentDispatcher;
        KeyNames.TryParse(settings.ToggleKey, out _toggleKey, out _);

        Groups = ActionCatalog.Groups
            .Select(g => new BindingGroupViewModel(g.Group, g.Actions.Select(a => _rows[a.Action] = new BindingRowViewModel(a, this)).ToList()))
            .ToList();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        Sync(() =>
        {
            SuppressMappedKeys = settings.SuppressMappedKeys;
            EnableOnStartup = settings.EnableOnStartup;
            ToggleBeep = settings.ToggleBeep;
            CloseToTray = settings.CloseToTray;
            StartMinimized = settings.StartMinimized;
        });
        _engine.ToggleBeep = settings.ToggleBeep;

        LoadInitialProfile();

        _engine.StateChanged += OnEngineStateChanged;
        _engine.EnabledChanged += enabled => _dispatcher.BeginInvoke(() =>
        {
            Sync(() => IsEmulationOn = enabled);
            RefreshStatus();
        });
        _engine.EnableRefused += () => _dispatcher.BeginInvoke(() =>
            ShowToast("Install the ViGEmBus driver first. Emulation can't start without it."));
        _engine.DriverStatusChanged += () => _dispatcher.BeginInvoke(RefreshStatus);
        RefreshStatus();
    }

    /// <summary>Raised when the emulation state, profile or hotkey changes, for the tray icon.</summary>
    public event Action? TrayStateChanged;

    public IReadOnlyList<BindingGroupViewModel> Groups { get; }
    public ObservableCollection<string> Profiles { get; } = [];
    public string? CurrentProfile { get; private set; }
    public string ToggleKeyLabel => KeyNames.Display(_toggleKey);
    public IReadOnlyList<ProfilePreset> Presets => ProfilePresets.All;
    public string ProfilesFolder => _profiles.Directory;
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

    [ObservableProperty]
    public partial string? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial bool IsEmulationOn { get; set; }

    [ObservableProperty]
    public partial ControllerSnapshot Snapshot { get; set; }

    [ObservableProperty]
    public partial ControllerAction? HighlightedAction { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "";

    [ObservableProperty]
    public partial string StatusDetail { get; set; } = "";

    [ObservableProperty]
    public partial string LiveViewHint { get; set; } = "";

    [ObservableProperty]
    public partial string DriverText { get; set; } = "";

    [ObservableProperty]
    public partial string ControllerText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsDriverReady { get; set; }

    [ObservableProperty]
    public partial bool IsDriverProblem { get; set; }

    [ObservableProperty]
    public partial bool SuppressMappedKeys { get; set; }

    [ObservableProperty]
    public partial bool EnableOnStartup { get; set; }

    [ObservableProperty]
    public partial bool ToggleBeep { get; set; }

    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool StartMinimized { get; set; }

    [ObservableProperty]
    public partial double WalkPercent { get; set; }

    [ObservableProperty]
    public partial bool IsCapturing { get; set; }

    [ObservableProperty]
    public partial string CaptureTitle { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPromptOpen { get; set; }

    [ObservableProperty]
    public partial string PromptTitle { get; set; } = "";

    [ObservableProperty]
    public partial string PromptMessage { get; set; } = "";

    [ObservableProperty]
    public partial string PromptText { get; set; } = "";

    [ObservableProperty]
    public partial bool PromptHasInput { get; set; }

    [ObservableProperty]
    public partial bool PromptShowCancel { get; set; }

    [ObservableProperty]
    public partial bool PromptIsDanger { get; set; }

    [ObservableProperty]
    public partial string PromptConfirmText { get; set; } = "OK";

    [ObservableProperty]
    public partial string? PromptError { get; set; }

    [ObservableProperty]
    public partial string ToastText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsToastVisible { get; set; }

    public bool IsAnyOverlayOpen => IsCapturing || IsPromptOpen;

    public async Task ConnectAsync()
    {
        await _engine.ConnectControllerAsync();
        if (_engine.HasController && _settings.EnableOnStartup)
            _engine.SetEnabled(true);
        await RefreshPlayerNumberLaterAsync();
    }

    public void SelectProfileFromTray(string name)
    {
        if (!IsAnyOverlayOpen)
            SelectedProfile = name;
    }

    public void ToggleEmulation()
    {
        if (!_engine.SetEnabled(!_engine.Enabled))
            ShowToast("Install the ViGEmBus driver first. Emulation can't start without it.");
    }

    // ---- Bindings ----

    public void StartCapture(BindingRowViewModel row)
    {
        if (IsPromptOpen)
            return;

        EndCapture();
        _captureRow = row;
        row.IsCapturing = true;
        CaptureTitle = row.FullName;
        IsCapturing = true;
        _engine.BeginCapture(vk => _dispatcher.BeginInvoke(() => OnKeyCaptured(vk)));
    }

    public void RemoveKey(int virtualKey)
    {
        if (_profile.Bindings.Remove(virtualKey))
            CommitProfileChange();
    }

    [RelayCommand]
    private void StartToggleKeyCapture()
    {
        if (IsPromptOpen)
            return;

        EndCapture();
        _capturingToggleKey = true;
        CaptureTitle = "On/off hotkey";
        IsCapturing = true;
        _engine.BeginCapture(vk => _dispatcher.BeginInvoke(() => OnKeyCaptured(vk)));
    }

    [RelayCommand]
    private void CancelCapture()
    {
        _engine.CancelCapture();
        EndCapture();
    }

    private void EndCapture()
    {
        if (_captureRow is not null)
            _captureRow.IsCapturing = false;
        _captureRow = null;
        _capturingToggleKey = false;
        IsCapturing = false;
    }

    private void OnKeyCaptured(int virtualKey)
    {
        if (!IsCapturing)
            return;

        var row = _captureRow;
        bool capturingToggle = _capturingToggleKey;
        EndCapture();
        string label = KeyNames.Display(virtualKey);

        if (capturingToggle)
        {
            if (virtualKey == _toggleKey)
                return;

            if (_profile.Bindings.TryGetValue(virtualKey, out var used))
            {
                ShowToast($"{label} is bound to {ActionCatalog.FullName(used)} in this profile. Remove that binding first.");
                return;
            }

            _toggleKey = virtualKey;
            _settings.ToggleKey = KeyNames.Describe(virtualKey);
            _settingsStore.Save(_settings);
            ApplyToEngine();
            OnPropertyChanged(nameof(ToggleKeyLabel));
            RefreshStatus();
            _log.Info($"Toggle key set to {_settings.ToggleKey}.");
            ShowToast($"The on/off hotkey is now {label}.");
            return;
        }

        if (row is null)
            return;

        if (virtualKey == _toggleKey)
        {
            ShowToast($"{label} is the on/off hotkey. Change the hotkey in Settings to use it here.");
            return;
        }

        if (_profile.Bindings.TryGetValue(virtualKey, out var previous))
        {
            if (previous == row.Action)
                return;
            ShowToast($"{label} moved from {ActionCatalog.FullName(previous)} to {row.FullName}.");
        }

        _profile.Bindings[virtualKey] = row.Action;
        CommitProfileChange();
    }

    // ---- Profiles ----

    partial void OnSelectedProfileChanged(string? oldValue, string? newValue)
    {
        if (_syncing || newValue is null || string.Equals(newValue, CurrentProfile, StringComparison.Ordinal))
            return;

        if (!TryLoadProfile(newValue, out string? error))
        {
            Sync(() => SelectedProfile = oldValue);
            _ = ShowMessageAsync("Couldn't open the profile", error!);
        }
    }

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        string? name = await ShowPromptAsync("New profile", "It starts with the default layout.",
            _profiles.UniqueName("New profile"), "Create", validator: n => _profiles.ValidateName(n));
        if (name is null)
            return;

        if (TrySaveProfile(name, ProfilePresets.Default.Create()))
            SwitchTo(name, $"Created profile {name}.");
    }

    [RelayCommand]
    private void AddPreset(ProfilePreset preset)
    {
        string name = _profiles.UniqueName(preset.Name);
        if (TrySaveProfile(name, preset.Create()))
            SwitchTo(name, $"Added the {preset.Name} layout as {name}.");
    }

    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        string? name = await ShowPromptAsync("Duplicate profile", $"Makes a copy of {CurrentProfile}.",
            _profiles.UniqueName($"{CurrentProfile} copy"), "Duplicate", validator: n => _profiles.ValidateName(n));
        if (name is null)
            return;

        if (TrySaveProfile(name, _profile.Clone()))
            SwitchTo(name, $"Created profile {name}.");
    }

    [RelayCommand]
    private async Task RenameProfileAsync()
    {
        string current = CurrentProfile!;
        string? name = await ShowPromptAsync("Rename profile", "", current, "Rename",
            validator: n => _profiles.ValidateName(n, renamingFrom: current));
        if (name is null || name == current)
            return;

        try
        {
            _profiles.Rename(current, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Could not rename profile {current}.", ex);
            await ShowMessageAsync("Couldn't rename the profile", ex.Message);
            return;
        }

        _log.Info($"Renamed profile {current} to {name}.");
        CurrentProfile = name;
        _settings.LastProfile = name;
        _settingsStore.Save(_settings);
        RefreshProfileList();
        TrayStateChanged?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        string current = CurrentProfile!;
        if (Profiles.Count <= 1)
        {
            ShowToast("You need at least one profile. Create another one before deleting this one.");
            return;
        }

        string? confirmed = await ShowPromptAsync($"Delete {current}?", "This removes the profile file. You can't undo this.",
            null, "Delete", danger: true);
        if (confirmed is null)
            return;

        try
        {
            _profiles.Delete(current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Could not delete profile {current}.", ex);
            await ShowMessageAsync("Couldn't delete the profile", ex.Message);
            return;
        }

        _log.Info($"Deleted profile {current}.");
        CurrentProfile = null;
        RefreshProfileList();
        LoadFirstWorkingProfile();
        ShowToast($"Deleted {current}.");
    }

    [RelayCommand]
    private async Task ResetProfileAsync()
    {
        var preset = ProfilePresets.ForProfile(CurrentProfile!) ?? ProfilePresets.Default;
        string layout = preset == ProfilePresets.Default ? "the default layout" : $"the {preset.Name} layout";
        string? confirmed = await ShowPromptAsync($"Reset {CurrentProfile}?",
            $"Replaces every binding in this profile with {layout}.", null, "Reset", danger: true);
        if (confirmed is null)
            return;

        _profile = preset.Create();
        CommitProfileChange();
        Sync(() => WalkPercent = Math.Round(_profile.WalkScale * 100));
        ShowToast($"Restored {layout}.");
    }

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import profile",
            Filter = "Profile files (*.json)|*.json|All files (*.*)|*.*",
        };
        if (!ShowFileDialog(dialog))
            return;

        try
        {
            string name = _profiles.Import(dialog.FileName);
            _log.Info($"Imported profile {name} from {dialog.FileName}.");
            SwitchTo(name, $"Imported {name}.");
        }
        catch (ConfigException ex)
        {
            await ShowMessageAsync("That file isn't a valid profile", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Couldn't import the profile", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export profile",
            FileName = $"{CurrentProfile}.json",
            Filter = "Profile files (*.json)|*.json",
        };
        if (!ShowFileDialog(dialog))
            return;

        try
        {
            _profiles.Export(CurrentProfile!, dialog.FileName);
            ShowToast($"Exported {CurrentProfile}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Couldn't export the profile", ex.Message);
        }
    }

    /// <summary>Pauses key capture so typing a file name isn't swallowed by the mapping.</summary>
    private bool ShowFileDialog(FileDialog dialog)
    {
        _engine.SetSuspended(true);
        try
        {
            return dialog.ShowDialog() == true;
        }
        finally
        {
            _engine.SetSuspended(false);
        }
    }

    [RelayCommand]
    private void OpenProfilesFolder() => OpenShell(_profiles.Directory);

    [RelayCommand]
    private void OpenLogFolder() => OpenShell(Path.GetDirectoryName(_log.FilePath)!);

    private void LoadInitialProfile()
    {
        AddNewPresets();
        RefreshProfileList();
        string lastName = _settings.LastProfile ?? ProfilePresets.Default.Name;
        string? last = Profiles.FirstOrDefault(p => string.Equals(p, lastName, StringComparison.OrdinalIgnoreCase));
        if (last is null || !TryLoadProfile(last, out _))
            LoadFirstWorkingProfile();
    }

    /// <summary>
    /// Adds a profile for each built-in layout this install hasn't added yet: all of them on first run,
    /// and the new ones after an update. A layout the user deleted isn't added again.
    /// </summary>
    private void AddNewPresets()
    {
        var added = _settings.AddedPresets ??= [];
        bool changed = false;
        foreach (var preset in ProfilePresets.All)
        {
            if (added.Contains(preset.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            if (_profiles.Exists(preset.Name) || TrySaveProfile(preset.Name, preset.Create()))
            {
                added.Add(preset.Name);
                changed = true;
            }
        }

        if (changed)
            _settingsStore.Save(_settings);
    }

    private void LoadFirstWorkingProfile()
    {
        foreach (string name in Profiles.ToList())
        {
            if (TryLoadProfile(name, out string? error))
                return;
            _log.Warn($"Skipping profile {name}: {error}");
        }

        // Every profile file is broken; start a fresh one rather than running without bindings.
        string fresh = _profiles.UniqueName("Default");
        if (TrySaveProfile(fresh, ProfilePresets.Default.Create()))
            SwitchTo(fresh, null);
    }

    private bool TryLoadProfile(string name, out string? error)
    {
        try
        {
            _profile = _profiles.Load(name);
        }
        catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            _log.Warn($"Could not load profile {name}: {ex.Message}");
            return false;
        }

        error = null;
        CurrentProfile = name;
        Sync(() =>
        {
            SelectedProfile = name;
            WalkPercent = Math.Round(_profile.WalkScale * 100);
        });
        RefreshRows();
        ApplyToEngine();

        _settings.LastProfile = name;
        _settingsStore.Save(_settings);
        _log.Info($"Loaded profile {name} ({_profile.Bindings.Count} bindings).");
        TrayStateChanged?.Invoke();
        return true;
    }

    private void SwitchTo(string name, string? toast)
    {
        RefreshProfileList();
        if (TryLoadProfile(name, out string? error))
        {
            if (toast is not null)
                ShowToast(toast);
        }
        else
        {
            _ = ShowMessageAsync("Couldn't open the profile", error!);
        }
    }

    private bool TrySaveProfile(string name, ProfileData profile)
    {
        try
        {
            _profiles.Save(name, profile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Could not save profile {name}.", ex);
            ShowToast($"Couldn't save the profile: {ex.Message}");
            return false;
        }
    }

    private void RefreshProfileList()
    {
        var names = _profiles.List();
        Sync(() =>
        {
            Profiles.Clear();
            foreach (string name in names)
                Profiles.Add(name);
            SelectedProfile = CurrentProfile;
        });
    }

    private void CommitProfileChange()
    {
        if (CurrentProfile is not null)
            TrySaveProfile(CurrentProfile, _profile);
        RefreshRows();
        ApplyToEngine();
    }

    private void RefreshRows()
    {
        foreach (var row in _rows.Values)
            row.Keys.Clear();

        foreach (var (vk, action) in _profile.Bindings.OrderBy(b => KeyNames.Display(b.Key), StringComparer.CurrentCulture))
            _rows[action].Keys.Add(new KeyChip(vk, KeyNames.Display(vk)));
    }

    private void ApplyToEngine()
    {
        var bindings = new Dictionary<int, ControllerAction>(_profile.Bindings);
        bindings.Remove(_toggleKey);

        _engine.ApplyProfile(new MappingProfile
        {
            ToggleKey = _toggleKey,
            SuppressMappedKeys = _settings.SuppressMappedKeys,
            WalkScale = _profile.WalkScale,
            Bindings = bindings,
        });
    }

    // ---- Settings ----

    partial void OnSuppressMappedKeysChanged(bool value)
    {
        if (_syncing)
            return;
        _settings.SuppressMappedKeys = value;
        _settingsStore.Save(_settings);
        ApplyToEngine();
    }

    partial void OnEnableOnStartupChanged(bool value) => SaveSetting(() => _settings.EnableOnStartup = value);

    partial void OnToggleBeepChanged(bool value)
    {
        _engine.ToggleBeep = value;
        SaveSetting(() => _settings.ToggleBeep = value);
    }

    partial void OnCloseToTrayChanged(bool value) => SaveSetting(() => _settings.CloseToTray = value);

    partial void OnStartMinimizedChanged(bool value) => SaveSetting(() => _settings.StartMinimized = value);

    partial void OnWalkPercentChanged(double value)
    {
        if (_syncing)
            return;

        _profile.WalkScale = Math.Clamp(value / 100.0, ProfileData.MinWalkScale, ProfileData.MaxWalkScale);
        CommitProfileChange();
    }

    private void SaveSetting(Action apply)
    {
        if (_syncing)
            return;
        apply();
        _settingsStore.Save(_settings);
    }

    // ---- Emulation and driver status ----

    partial void OnIsEmulationOnChanged(bool value)
    {
        if (_syncing)
            return;

        if (!_engine.SetEnabled(value))
        {
            _dispatcher.BeginInvoke(() => Sync(() => IsEmulationOn = false));
            ShowToast("Install the ViGEmBus driver first. Emulation can't start without it.");
        }
    }

    [RelayCommand]
    private void OpenDriverPage() => OpenShell(DriverDownloadUrl);

    [RelayCommand]
    private async Task RetryDriverAsync()
    {
        await _engine.ConnectControllerAsync();
        if (_engine.HasController)
            ShowToast("Virtual controller connected.");
        await RefreshPlayerNumberLaterAsync();
    }

    private async Task RefreshPlayerNumberLaterAsync()
    {
        // Windows assigns the player slot shortly after the controller is plugged in.
        for (int i = 0; i < 6 && _engine.HasController && _engine.PlayerNumber is null; i++)
            await Task.Delay(500);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        bool on = _engine.Enabled;
        var status = _engine.DriverStatus;
        IsDriverReady = status == DriverStatus.Ready;
        IsDriverProblem = status is DriverStatus.NotInstalled or DriverStatus.Failed;

        StatusTitle = on ? "Emulation is on" : "Emulation is off";
        LiveViewHint = on ? "Press a mapped key to see it here" : "Turn emulation on to test your keys";
        StatusDetail = status switch
        {
            DriverStatus.Ready => on
                ? $"Games see your keyboard as a controller. Press {ToggleKeyLabel} to stop."
                : $"Press {ToggleKeyLabel} anywhere, or use the switch, to start.",
            DriverStatus.Checking => "Connecting the virtual controller…",
            DriverStatus.NotInstalled => "The ViGEmBus driver is required. Install it, then select Try again.",
            _ => "The virtual controller couldn't be created.",
        };

        DriverText = status switch
        {
            DriverStatus.Ready => "ViGEmBus driver installed",
            DriverStatus.Checking => "Checking the driver…",
            DriverStatus.NotInstalled => "ViGEmBus driver not installed",
            _ => $"Driver error: {_engine.DriverError}",
        };

        ControllerText = _engine.HasController
            ? _engine.PlayerNumber is { } player ? $"Virtual Xbox 360 controller, player {player}" : "Virtual Xbox 360 controller connected"
            : "No virtual controller";

        TrayStateChanged?.Invoke();
    }

    private void OnEngineStateChanged(ControllerSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            _latestSnapshot = snapshot;
            if (_snapshotQueued)
                return;
            _snapshotQueued = true;
        }

        // Coalesce bursts of key events into one UI update per rendered frame.
        _dispatcher.BeginInvoke(DispatcherPriority.Render, ApplySnapshot);
    }

    private void ApplySnapshot()
    {
        ControllerSnapshot snapshot;
        lock (_snapshotLock)
        {
            snapshot = _latestSnapshot;
            _snapshotQueued = false;
        }

        Snapshot = snapshot;
        foreach (var row in _rows.Values)
            row.IsActive = snapshot.IsActive(row.Action);
    }

    // ---- Prompt and toast ----

    public Task<string?> ShowPromptAsync(string title, string message, string? text, string confirmText,
        bool danger = false, Func<string, string?>? validator = null, bool showCancel = true)
    {
        CancelCapture();
        _prompt?.TrySetResult(null);

        PromptTitle = title;
        PromptMessage = message;
        PromptHasInput = text is not null;
        PromptText = text ?? "";
        PromptConfirmText = confirmText;
        PromptIsDanger = danger;
        PromptShowCancel = showCancel;
        PromptError = null;
        _promptValidator = validator;
        IsPromptOpen = true;
        _engine.SetSuspended(true);

        _prompt = new TaskCompletionSource<string?>();
        return _prompt.Task;
    }

    public Task ShowMessageAsync(string title, string message) =>
        ShowPromptAsync(title, message, null, "OK", showCancel: false);

    [RelayCommand]
    private void ConfirmPrompt()
    {
        if (PromptHasInput && _promptValidator?.Invoke(PromptText) is { } error)
        {
            PromptError = error;
            return;
        }

        ClosePrompt(PromptHasInput ? PromptText : "");
    }

    [RelayCommand]
    private void CancelPrompt() => ClosePrompt(null);

    private void ClosePrompt(string? result)
    {
        IsPromptOpen = false;
        _engine.SetSuspended(false);
        var prompt = _prompt;
        _prompt = null;
        prompt?.TrySetResult(result);
    }

    partial void OnPromptTextChanged(string value) => PromptError = null;

    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(IsAnyOverlayOpen));

    partial void OnIsPromptOpenChanged(bool value) => OnPropertyChanged(nameof(IsAnyOverlayOpen));

    public void ShowToast(string text)
    {
        ToastText = text;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error($"Could not open {target}.", ex);
            ShowToast($"Couldn't open {target}.");
        }
    }

    private void Sync(Action update)
    {
        bool wasSyncing = _syncing;
        _syncing = true;
        try
        {
            update();
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }
}
