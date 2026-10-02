namespace Keyvert.Core;

/// <summary>Button bits, using the XInput wButtons layout so they can be submitted as-is.</summary>
[Flags]
public enum PadButtons : ushort
{
    None = 0,
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LS = 0x0040,
    RS = 0x0080,
    LB = 0x0100,
    RB = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

public readonly record struct ControllerState(
    PadButtons Buttons,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    byte LeftTrigger,
    byte RightTrigger)
{
    public static ControllerState Neutral => default;
}
