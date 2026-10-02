namespace Keyvert.Core;

public enum ControllerAction
{
    A,
    B,
    X,
    Y,
    LB,
    RB,
    LT,
    RT,
    LS,
    RS,
    Back,
    Start,
    Guide,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    LeftStickUp,
    LeftStickDown,
    LeftStickLeft,
    LeftStickRight,
    RightStickUp,
    RightStickDown,
    RightStickLeft,
    RightStickRight,

    /// <summary>While held, scales the left stick by the profile's walk scale.</summary>
    Walk,
}

public static class ControllerActions
{
    private static readonly Dictionary<string, ControllerAction> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LeftShoulder"] = ControllerAction.LB,
        ["RightShoulder"] = ControllerAction.RB,
        ["LeftTrigger"] = ControllerAction.LT,
        ["RightTrigger"] = ControllerAction.RT,
        ["LeftThumb"] = ControllerAction.LS,
        ["RightThumb"] = ControllerAction.RS,
        ["L3"] = ControllerAction.LS,
        ["R3"] = ControllerAction.RS,
        ["Select"] = ControllerAction.Back,
        ["View"] = ControllerAction.Back,
        ["Menu"] = ControllerAction.Start,
    };

    public static bool TryParse(string name, out ControllerAction action)
    {
        name = name.Trim();
        if (Aliases.TryGetValue(name, out action))
            return true;

        // Enum.TryParse also accepts numbers and comma lists, which would silently map to another action.
        return !int.TryParse(name, out _)
            && !name.Contains(',')
            && Enum.TryParse(name, ignoreCase: true, out action)
            && Enum.IsDefined(action);
    }

    public static PadButtons ToButton(ControllerAction action) => action switch
    {
        ControllerAction.A => PadButtons.A,
        ControllerAction.B => PadButtons.B,
        ControllerAction.X => PadButtons.X,
        ControllerAction.Y => PadButtons.Y,
        ControllerAction.LB => PadButtons.LB,
        ControllerAction.RB => PadButtons.RB,
        ControllerAction.LS => PadButtons.LS,
        ControllerAction.RS => PadButtons.RS,
        ControllerAction.Back => PadButtons.Back,
        ControllerAction.Start => PadButtons.Start,
        ControllerAction.Guide => PadButtons.Guide,
        ControllerAction.DpadUp => PadButtons.DpadUp,
        ControllerAction.DpadDown => PadButtons.DpadDown,
        ControllerAction.DpadLeft => PadButtons.DpadLeft,
        ControllerAction.DpadRight => PadButtons.DpadRight,
        _ => PadButtons.None,
    };
}
