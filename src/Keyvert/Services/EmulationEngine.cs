using Keyvert.Core;
using Nefarius.ViGEm.Client.Exceptions;

namespace Keyvert.Services;

public enum DriverStatus
{
    Checking,
    Ready,
    NotInstalled,
    Failed,
}

public readonly record struct ControllerSnapshot(ControllerState State, ulong ActiveActions)
{
    public bool IsActive(ControllerAction action) => (ActiveActions & (1UL << (int)action)) != 0;
}

/// <summary>
/// Owns the keyboard hook, the input router and the virtual controller.
/// Key events arrive on the hook thread; everything else is called from the UI thread.
/// Events may be raised on either thread.
/// </summary>
public sealed class EmulationEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly AppLog _log;
    private readonly HashSet<int> _captureSwallowed = [];
    private KeyboardHook? _hook;
    private VirtualController? _controller;
    private MappingProfile _profile;
    private InputRouter _router;
    private Action<int>? _captureHandler;
    private bool _toggleBlockedHeld;
    private bool _connecting;
    private bool _disposed;

    public EmulationEngine(MappingProfile profile, AppLog log)
    {
        _profile = profile;
        _router = new InputRouter(profile);
        _router.SetEnabled(false);
        _log = log;
    }

    public event Action<ControllerSnapshot>? StateChanged;

    /// <summary>Raised when emulation turns on or off, including from the hotkey.</summary>
    public event Action<bool>? EnabledChanged;

    /// <summary>Raised when the hotkey is pressed but no virtual controller exists.</summary>
    public event Action? EnableRefused;

    public event Action? DriverStatusChanged;

    public bool ToggleBeep { get; set; } = true;

    public DriverStatus DriverStatus { get; private set; } = DriverStatus.Checking;

    public string? DriverError { get; private set; }

    public int? PlayerNumber => _controller?.PlayerNumber;

    public bool HasController
    {
        get { lock (_gate) return _controller is not null; }
    }

    public bool Enabled
    {
        get { lock (_gate) return _router.Enabled; }
    }

    /// <exception cref="System.ComponentModel.Win32Exception">The hook could not be installed.</exception>
    public void Start()
    {
        _hook = new KeyboardHook(OnKey, ex => _log.Error("Error while handling a key event.", ex));
        _log.Info("Keyboard hook installed.");
    }

    public async Task ConnectControllerAsync()
    {
        lock (_gate)
        {
            if (_controller is not null || _connecting || _disposed)
                return;
            _connecting = true;
        }

        SetDriverStatus(DriverStatus.Checking, null);
        try
        {
            var controller = await Task.Run(() => new VirtualController());
            controller.SubmitFailed += ex => _log.Error("The driver rejected a controller report.", ex);

            lock (_gate)
            {
                if (_disposed)
                {
                    controller.Dispose();
                    return;
                }
                _controller = controller;
            }

            _log.Info("Virtual Xbox 360 controller connected.");
            SetDriverStatus(DriverStatus.Ready, null);
        }
        catch (VigemBusNotFoundException)
        {
            _log.Warn("ViGEmBus driver not found.");
            SetDriverStatus(DriverStatus.NotInstalled, null);
        }
        catch (Exception ex)
        {
            _log.Error("Could not create the virtual controller.", ex);
            SetDriverStatus(DriverStatus.Failed, ex.Message);
        }
        finally
        {
            lock (_gate)
                _connecting = false;
        }
    }

    /// <returns>False if emulation can't be turned on because there is no virtual controller.</returns>
    public bool SetEnabled(bool enabled)
    {
        ControllerSnapshot? snapshot;
        lock (_gate)
        {
            if (enabled && _controller is null)
                return false;
            if (_router.Enabled == enabled)
                return true;

            snapshot = _router.SetEnabled(enabled) ? Publish() : null;
        }

        OnEnabledChanged(enabled, snapshot);
        return true;
    }

    /// <summary>Stops capturing keys while the user types into the app's own text boxes.</summary>
    public void SetSuspended(bool suspended)
    {
        ControllerSnapshot? snapshot;
        lock (_gate)
            snapshot = _router.SetSuspended(suspended) ? Publish() : null;

        RaiseState(snapshot);
    }

    /// <summary>Returns the controller to neutral, e.g. when the session is locked and key-ups may never arrive.</summary>
    public void ReleaseAll()
    {
        ControllerSnapshot? snapshot;
        lock (_gate)
            snapshot = _router.ReleaseAll() ? Publish() : null;

        RaiseState(snapshot);
    }

    public void ApplyProfile(MappingProfile profile)
    {
        ControllerSnapshot snapshot;
        lock (_gate)
        {
            bool enabled = _router.Enabled;
            bool suspended = _router.Suspended;
            _router.ReleaseAll();

            _profile = profile;
            _router = new InputRouter(profile);
            _router.SetEnabled(enabled);
            _router.SetSuspended(suspended);
            snapshot = Publish();
        }

        RaiseState(snapshot);
    }

    /// <summary>
    /// The next key pressed anywhere goes to <paramref name="handler"/> (on the hook thread)
    /// instead of the game or the controller.
    /// </summary>
    public void BeginCapture(Action<int> handler)
    {
        lock (_gate)
            _captureHandler = handler;
    }

    public void CancelCapture()
    {
        lock (_gate)
            _captureHandler = null;
    }

    public void Dispose()
    {
        VirtualController? controller;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            controller = _controller;
            _controller = null;
        }

        _hook?.Dispose();
        controller?.Dispose();
        _log.Info("Emulation engine stopped, controller released.");
    }

    private bool OnKey(int virtualKey, bool isDown)
    {
        ControllerSnapshot? snapshot = null;
        bool? enabledNow = null;
        bool refused = false;
        bool suppress;

        lock (_gate)
        {
            if (isDown && _captureHandler is { } handler)
            {
                _captureHandler = null;
                _captureSwallowed.Add(virtualKey);
                ThreadPool.QueueUserWorkItem(_ => handler(virtualKey));
                return true;
            }

            if (_captureSwallowed.Contains(virtualKey))
            {
                if (!isDown)
                    _captureSwallowed.Remove(virtualKey);
                return true;
            }

            if (virtualKey == _profile.ToggleKey && _controller is null && !_router.Enabled)
            {
                refused = isDown && !_toggleBlockedHeld;
                _toggleBlockedHeld = isDown;
                suppress = true;
            }
            else
            {
                var result = _router.Process(virtualKey, isDown);
                if (result.StateChanged)
                    snapshot = Publish();
                if (result.EnabledChanged)
                    enabledNow = _router.Enabled;
                suppress = result.Suppress;
            }
        }

        if (refused)
            EnableRefused?.Invoke();

        if (enabledNow is { } enabled)
            OnEnabledChanged(enabled, snapshot);
        else
            RaiseState(snapshot);

        return suppress;
    }

    /// <summary>Sends the router's state to the controller. Call inside the lock.</summary>
    private ControllerSnapshot Publish()
    {
        var snapshot = new ControllerSnapshot(_router.BuildState(), _router.ActiveActions());
        _controller?.Update(snapshot.State);
        return snapshot;
    }

    private void OnEnabledChanged(bool enabled, ControllerSnapshot? snapshot)
    {
        RaiseState(snapshot);
        _log.Info(enabled ? "Emulation on." : "Emulation off.");
        EnabledChanged?.Invoke(enabled);

        if (ToggleBeep)
        {
            int frequency = enabled ? 1000 : 500;
            ThreadPool.QueueUserWorkItem(_ => Console.Beep(frequency, 70));
        }
    }

    private void RaiseState(ControllerSnapshot? snapshot)
    {
        if (snapshot is { } value)
            StateChanged?.Invoke(value);
    }

    private void SetDriverStatus(DriverStatus status, string? error)
    {
        DriverStatus = status;
        DriverError = error;
        DriverStatusChanged?.Invoke();
    }
}
