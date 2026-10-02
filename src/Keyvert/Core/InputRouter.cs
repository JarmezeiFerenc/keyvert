namespace Keyvert.Core;

public readonly record struct RouteResult(bool Suppress, bool StateChanged, bool EnabledChanged);

/// <summary>
/// Decides, for every physical key event, whether it feeds the virtual controller,
/// passes through to the focused app, or both, and tracks the resulting controller state.
/// Not thread-safe; the caller serializes access.
/// </summary>
/// <remarks>
/// A key is owned by whichever side saw its key-down. That keeps key-down/key-up pairs
/// balanced on both sides when the mapping is toggled while keys are held, so neither the
/// game's keyboard input nor the controller is left with a stuck key.
/// </remarks>
public sealed class InputRouter
{
    private readonly MappingProfile _profile;
    private readonly HashSet<int> _captured = [];
    private readonly HashSet<int> _passedThrough = [];
    private readonly HashSet<int> _releasing = [];
    private bool _toggleHeld;

    public InputRouter(MappingProfile profile)
    {
        _profile = profile;
        Enabled = profile.StartEnabled;
    }

    /// <summary>The user's on/off switch, flipped by the toggle key.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Temporarily stops capturing keys without changing <see cref="Enabled"/>.</summary>
    public bool Suspended { get; private set; }

    public bool IsCapturing => Enabled && !Suspended;

    public RouteResult Process(int virtualKey, bool isDown)
    {
        if (virtualKey == _profile.ToggleKey)
            return ProcessToggle(isDown);

        bool suppress = _profile.SuppressMappedKeys;

        if (isDown)
        {
            if (_passedThrough.Contains(virtualKey))
                return default;

            if (_captured.Contains(virtualKey) || _releasing.Contains(virtualKey))
                return new RouteResult(suppress, false, false);

            if (IsCapturing && _profile.Bindings.ContainsKey(virtualKey))
            {
                _captured.Add(virtualKey);
                return new RouteResult(suppress, true, false);
            }

            _passedThrough.Add(virtualKey);
            return default;
        }

        if (_captured.Remove(virtualKey))
            return new RouteResult(suppress, true, false);

        if (_releasing.Remove(virtualKey))
            return new RouteResult(suppress, false, false);

        _passedThrough.Remove(virtualKey);
        return default;
    }

    /// <returns>Whether the controller state changed.</returns>
    public bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;

        Enabled = enabled;
        return !enabled && ReleaseAll();
    }

    /// <returns>Whether the controller state changed.</returns>
    public bool SetSuspended(bool suspended)
    {
        if (Suspended == suspended)
            return false;

        Suspended = suspended;
        return suspended && ReleaseAll();
    }

    /// <summary>
    /// Returns the controller to neutral. Keys still physically held stay owned by the
    /// controller side until released, so their key-ups don't leak to the focused app.
    /// </summary>
    /// <returns>Whether anything was released.</returns>
    public bool ReleaseAll()
    {
        if (_captured.Count == 0)
            return false;

        _releasing.UnionWith(_captured);
        _captured.Clear();
        return true;
    }

    /// <summary>Bit <c>1 &lt;&lt; (int)action</c> is set for every action whose key is held.</summary>
    public ulong ActiveActions()
    {
        ulong mask = 0;
        foreach (int vk in _captured)
            mask |= 1UL << (int)_profile.Bindings[vk];
        return mask;
    }

    public ControllerState BuildState()
    {
        var buttons = PadButtons.None;
        bool lt = false, rt = false, walk = false;
        bool lUp = false, lDown = false, lLeft = false, lRight = false;
        bool rUp = false, rDown = false, rLeft = false, rRight = false;

        foreach (int vk in _captured)
        {
            switch (_profile.Bindings[vk])
            {
                case ControllerAction.LeftStickUp: lUp = true; break;
                case ControllerAction.LeftStickDown: lDown = true; break;
                case ControllerAction.LeftStickLeft: lLeft = true; break;
                case ControllerAction.LeftStickRight: lRight = true; break;
                case ControllerAction.RightStickUp: rUp = true; break;
                case ControllerAction.RightStickDown: rDown = true; break;
                case ControllerAction.RightStickLeft: rLeft = true; break;
                case ControllerAction.RightStickRight: rRight = true; break;
                case ControllerAction.LT: lt = true; break;
                case ControllerAction.RT: rt = true; break;
                case ControllerAction.Walk: walk = true; break;
                case var action: buttons |= ControllerActions.ToButton(action); break;
            }
        }

        var (leftX, leftY) = StickValues(lLeft, lRight, lDown, lUp, walk ? _profile.WalkScale : 1.0);
        var (rightX, rightY) = StickValues(rLeft, rRight, rDown, rUp, 1.0);

        return new ControllerState(
            buttons,
            leftX, leftY,
            rightX, rightY,
            lt ? byte.MaxValue : (byte)0,
            rt ? byte.MaxValue : (byte)0);
    }

    private RouteResult ProcessToggle(bool isDown)
    {
        if (!isDown)
        {
            _toggleHeld = false;
            return new RouteResult(true, false, false);
        }

        if (_toggleHeld)
            return new RouteResult(true, false, false);

        _toggleHeld = true;
        bool stateChanged = SetEnabled(!Enabled);
        return new RouteResult(true, stateChanged, true);
    }

    private static (short X, short Y) StickValues(bool negX, bool posX, bool negY, bool posY, double scale)
    {
        int x = (posX ? 1 : 0) - (negX ? 1 : 0);
        int y = (posY ? 1 : 0) - (negY ? 1 : 0);
        if (x == 0 && y == 0)
            return (0, 0);

        // Keep diagonals on the unit circle, like a physical stick's round gate.
        double length = Math.Sqrt(x * x + y * y);
        double factor = scale / length * short.MaxValue;
        return ((short)Math.Round(x * factor), (short)Math.Round(y * factor));
    }
}
