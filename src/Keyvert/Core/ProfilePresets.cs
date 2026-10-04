using System.Text.RegularExpressions;

namespace Keyvert.Core;

/// <summary>A built-in layout for one kind of game.</summary>
public sealed record ProfilePreset(string Name, string Description, string Json)
{
    public ProfileData Create() => ProfileSerializer.Parse(Json);
}

public static partial class ProfilePresets
{
    /// <summary>
    /// Works for most games without a mouse: the left hand moves and takes the common PC keys
    /// (Space jump, E interact, R reload, Shift sprint, Ctrl crouch), the right hand turns the camera on
    /// IJKL with the triggers above it, and the arrow keys drive the D-pad so menus work as expected.
    /// </summary>
    public static readonly ProfilePreset Default = new(
        "Default",
        "For most games. WASD moves, IJKL turns the camera, U and O are the triggers, arrow keys are the D-pad.",
        """
        {
          "walkScale": 0.5,
          "bindings": {
            "W": "LeftStickUp", "A": "LeftStickLeft", "S": "LeftStickDown", "D": "LeftStickRight",
            "LeftShift": "LS",
            "I": "RightStickUp", "J": "RightStickLeft", "K": "RightStickDown", "L": "RightStickRight",
            "H": "RS",
            "Space": "A", "Enter": "A",
            "LeftCtrl": "B", "Backspace": "B",
            "E": "X",
            "R": "Y",
            "Q": "LB", "F": "RB",
            "U": "LT", "O": "RT",
            "Up": "DpadUp", "Down": "DpadDown", "Left": "DpadLeft", "Right": "DpadRight",
            "Esc": "Start", "Tab": "Back"
          }
        }
        """);

    public static readonly ProfilePreset Action = new(
        "Action (Souls-like)",
        "Third-person action. Space dodges, Q locks on, U/O/Y/P are bumpers and triggers, Ctrl walks.",
        """
        {
          "walkScale": 0.4,
          "bindings": {
            "W": "LeftStickUp", "A": "LeftStickLeft", "S": "LeftStickDown", "D": "LeftStickRight",
            "C": "LS", "LeftCtrl": "Walk",
            "I": "RightStickUp", "J": "RightStickLeft", "K": "RightStickDown", "L": "RightStickRight",
            "Q": "RS",
            "E": "A", "Enter": "A",
            "Space": "B", "Backspace": "B",
            "R": "X",
            "F": "Y",
            "U": "LB", "O": "RB",
            "Y": "LT", "P": "RT",
            "Up": "DpadUp", "Down": "DpadDown", "Left": "DpadLeft", "Right": "DpadRight",
            "Esc": "Start", "Tab": "Back"
          }
        }
        """);

    public static readonly ProfilePreset Platformer = new(
        "Platformer",
        "2D platformers. Arrow keys move, Z jumps, X attacks, C dashes, Shift is the right trigger.",
        """
        {
          "walkScale": 0.5,
          "bindings": {
            "Up": "LeftStickUp", "Left": "LeftStickLeft", "Down": "LeftStickDown", "Right": "LeftStickRight",
            "Z": "A", "Space": "A", "Enter": "A",
            "C": "B", "Backspace": "B",
            "X": "X",
            "V": "Y",
            "A": "LB", "S": "RB",
            "D": "LT", "F": "RT", "LeftShift": "RT",
            "Esc": "Start", "Tab": "Back"
          }
        }
        """);

    public static readonly ProfilePreset Racing = new(
        "Racing",
        "Driving games. W/S are throttle and brake, A/D steer, Space is the handbrake, hold Shift to steer gently.",
        """
        {
          "walkScale": 0.4,
          "bindings": {
            "A": "LeftStickLeft", "D": "LeftStickRight",
            "LeftShift": "Walk",
            "W": "RT", "S": "LT",
            "Space": "A", "Enter": "A",
            "E": "B", "Backspace": "B",
            "Q": "X",
            "R": "Y",
            "LeftCtrl": "LB", "C": "RB",
            "F": "RS",
            "Up": "DpadUp", "Down": "DpadDown", "Left": "DpadLeft", "Right": "DpadRight",
            "Esc": "Start", "Tab": "Back"
          }
        }
        """);

    public static readonly ProfilePreset TwinStick = new(
        "Twin-stick shooter",
        "Top-down shooters. WASD moves, arrow keys aim, Space fires, Shift is the left trigger, 1-4 are the D-pad.",
        """
        {
          "walkScale": 0.5,
          "bindings": {
            "W": "LeftStickUp", "A": "LeftStickLeft", "S": "LeftStickDown", "D": "LeftStickRight",
            "Up": "RightStickUp", "Left": "RightStickLeft", "Down": "RightStickDown", "Right": "RightStickRight",
            "Space": "RT", "LeftShift": "LT",
            "E": "A", "Enter": "A",
            "LeftCtrl": "B", "Backspace": "B",
            "R": "X",
            "F": "Y",
            "Q": "LB", "G": "RB",
            "1": "DpadUp", "2": "DpadDown", "3": "DpadLeft", "4": "DpadRight",
            "Esc": "Start", "Tab": "Back"
          }
        }
        """);

    public static readonly ProfilePreset MinecraftDungeons = new(
        "Minecraft Dungeons",
        "Minecraft Dungeons. Space, 2, O and 1 are A, B, X and Y.",
        """
        {
          "walkScale": 0.5,
          "bindings": {
            "W": "LeftStickUp", "A": "LeftStickLeft", "S": "LeftStickDown", "D": "LeftStickRight",
            "Space": "A", "2": "B", "O": "X", "1": "Y",
            "R": "LB", "3": "RB", "E": "LT", "P": "RT",
            "Q": "LS", "G": "RS",
            "I": "DpadUp", "F": "DpadDown", "X": "DpadLeft", "J": "DpadRight",
            "K": "Start", "M": "Back"
          }
        }
        """);

    public static readonly IReadOnlyList<ProfilePreset> All = [Default, Action, Platformer, Racing, TwinStick, MinecraftDungeons];

    /// <summary>The preset a profile was made from, judged by its name ("Racing" or "Racing (2)").</summary>
    public static ProfilePreset? ForProfile(string profileName)
    {
        string baseName = NumberSuffix().Replace(profileName, "");
        return All.FirstOrDefault(p => string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@" \(\d+\)$")]
    private static partial Regex NumberSuffix();
}
