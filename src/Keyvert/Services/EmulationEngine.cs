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
    private readonly Timer _smoothTimer;
    private ControllerState _targetState;
    private ControllerState _currentState;
    private const int UpdateIntervalMs = 15;

    public EmulationEngine(MappingProfile profile, AppLog log)
    {
        _profile = profile;
        _router = new InputRouter(profile);
        _router.SetEnabled(false);
        _log = log;

        // Start Tick Loop for Smoothing
        _smoothTimer = new Timer(OnSmoothTick, null, 0, UpdateIntervalMs);
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

        _smoothTimer?.Dispose();
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
        _targetState = _router.BuildState();

        var snapshot = new ControllerSnapshot(_targetState, _router.ActiveActions());
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

    private void OnSmoothTick(object? state)
    {
        lock (_gate)
        {
            if (_controller is null || !_router.Enabled)
                return;

            float smoothMs = (float)_profile.SmoothTime * 100f;

            // Calculate lerp factor based on target duration
            float lerpFactor;
            if (smoothMs <= 0f)
            {
                lerpFactor = 1.0f; // Instant snap
            }
            else
            {
                lerpFactor = Math.Clamp(UpdateIntervalMs / smoothMs, 0.02f, 1.0f);
            }

            float thumbStickLerp = (_profile.SmoothInput == 1 || _profile.SmoothInput == 3) ? lerpFactor : 1f;
            float triggerLerp = (_profile.SmoothInput == 2 || _profile.SmoothInput == 3) ? lerpFactor : 1f;

            // Smooth sticks with easing
            var left = LerpJoystick(_currentState.LeftX, _currentState.LeftY, _targetState.LeftX, _targetState.LeftY, thumbStickLerp);
            _currentState.LeftX = left.X;
            _currentState.LeftY = left.Y;

            var right = LerpJoystick(_currentState.RightX, _currentState.RightY, _targetState.RightX, _targetState.RightY, thumbStickLerp);
            _currentState.RightX = right.X;
            _currentState.RightY = right.Y;

            // Smooth triggers with easing
            _currentState.LeftTrigger = LerpTrigger(_currentState.LeftTrigger, _targetState.LeftTrigger, triggerLerp);
            _currentState.RightTrigger = LerpTrigger(_currentState.RightTrigger, _targetState.RightTrigger, triggerLerp);

            // Buttons are digital, so they snap immediately
            _currentState.Buttons = _targetState.Buttons;

            _controller.Update(_currentState);
            StateChanged?.Invoke(new ControllerSnapshot(_currentState, _router.ActiveActions()));
        }
    }

    private static (short X, short Y) LerpJoystick(short currentX, short currentY, short targetX, short targetY, float baseFactor)
    {
        if (currentX == targetX && currentY == targetY) return (targetX, targetY);
        if (baseFactor >= 1.0f) return (targetX, targetY);

        int diffX = targetX - currentX;
        int diffY = targetY - currentY;
        double distance = Math.Sqrt((double)diffX * diffX + (double)diffY * diffY);

        if (distance <= 2.0)
            return (targetX, targetY);

        bool isReleasing = (targetX == 0 && targetY == 0);

        // Clean release deadzone near center (prevents asymptotic slow creep)
        if (isReleasing && distance < 400.0)
            return (0, 0);

        // Jump Halfway when first pressing for responsive feel
        bool isStartingPress = (currentX == 0 && currentY == 0 && !isReleasing);
        if (isStartingPress)
        {
            return ((short)(targetX / 2), (short)(targetY / 2));
        }

        // Detect Flicks
        bool isReversingX = (currentX > 0 && targetX < 0) || (currentX < 0 && targetX > 0);
        bool isReversingY = (currentY > 0 && targetY < 0) || (currentY < 0 && targetY > 0);
        bool isDirectionFlip = isReversingX || isReversingY;

        float easedFactor;

        if (isReleasing)
        {
            // Realistic spring release mimicking thumbstick spring letting go
            float progress = Math.Clamp((float)(distance / 32768.0), 0f, 1f);
            easedFactor = baseFactor * (1.5f + (2.5f * progress));
        }
        else if (isDirectionFlip)
        {
            // Flick
            easedFactor = Math.Max(baseFactor * 2.5f, 0.6f);
        }
        else
        {
            // Normal Movement
            double currentMagnitude = Math.Sqrt((double)currentX * currentX + (double)currentY * currentY);
            float travelFromCenter = Math.Clamp((float)(currentMagnitude / 32768.0), 0f, 1f);
            easedFactor = baseFactor * (0.5f + (0.5f * travelFromCenter));
        }

        easedFactor = Math.Clamp(easedFactor, 0.05f, 1.0f);

        // Calculate step magnitude
        double stepMagnitude = distance * easedFactor;
        if (stepMagnitude < 1.0) stepMagnitude = 1.0;

        if (stepMagnitude >= distance)
            return (targetX, targetY);

        // Move X and Y proportionally
        double ratio = stepMagnitude / distance;
        short newX = (short)(currentX + (int)(diffX * ratio));
        short newY = (short)(currentY + (int)(diffY * ratio));

        return (newX, newY);
    }

    private static byte LerpTrigger(byte current, byte target, float baseFactor)
    {
        if (current == target) return target;
        if (baseFactor >= 1.0f) return target;

        int diff = target - current;
        int absDiff = Math.Abs(diff);

        bool isReleasing = (target == 0);

        // Trigger snap to zero near the bottom of travel
        if (isReleasing && absDiff < 12)
            return 0;

        // Jump Halfway when first pressing for responsive feel
        bool isStartingPress = (current == 0 && !isReleasing);
        if (isStartingPress)
        {
            return (byte)(target / 2);
        }

        if (absDiff <= 1)
            return target;

        float easedFactor;

        if (isReleasing)
        {
            // Realistic spring release mimicking thumbstick spring letting go
            float progress = Math.Clamp((float)absDiff / 255f, 0f, 1f);
            easedFactor = baseFactor * (1.2f + (1.5f * progress));
        }
        else
        {
            // Smooth Movement
            float travelFromZero = Math.Clamp((float)current / 255f, 0f, 1f);
            easedFactor = baseFactor * (0.3f + (0.7f * travelFromZero));
        }

        easedFactor = Math.Clamp(easedFactor, 0.05f, 1.0f);

        int step = (int)(diff * easedFactor);
        if (step == 0) step = Math.Sign(diff);

        if (Math.Abs(step) > absDiff) return target;

        return (byte)(current + step);
    }

}
