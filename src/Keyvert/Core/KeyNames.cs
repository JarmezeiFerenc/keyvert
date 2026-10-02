using System.Runtime.InteropServices;
using Keys = System.Windows.Forms.Keys;

namespace Keyvert.Core;

/// <summary>Converts key names from the config file to Windows virtual-key codes.</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, Keys> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LeftShift"] = Keys.LShiftKey,
        ["RightShift"] = Keys.RShiftKey,
        ["LeftCtrl"] = Keys.LControlKey,
        ["RightCtrl"] = Keys.RControlKey,
        ["LeftControl"] = Keys.LControlKey,
        ["RightControl"] = Keys.RControlKey,
        ["LeftAlt"] = Keys.LMenu,
        ["RightAlt"] = Keys.RMenu,
        ["LeftWin"] = Keys.LWin,
        ["RightWin"] = Keys.RWin,
        ["Esc"] = Keys.Escape,
        ["Backspace"] = Keys.Back,
        ["Del"] = Keys.Delete,
        ["Ins"] = Keys.Insert,
        ["PgUp"] = Keys.PageUp,
        ["PgDn"] = Keys.PageDown,
        ["Comma"] = Keys.Oemcomma,
        ["Period"] = Keys.OemPeriod,
        ["Minus"] = Keys.OemMinus,
        ["Plus"] = Keys.Oemplus,
        ["Semicolon"] = Keys.OemSemicolon,
        ["Quote"] = Keys.OemQuotes,
        ["Slash"] = Keys.OemQuestion,
        ["Backslash"] = Keys.OemBackslash,
        ["LeftBracket"] = Keys.OemOpenBrackets,
        ["RightBracket"] = Keys.OemCloseBrackets,
        ["Tilde"] = Keys.Oemtilde,
        ["Backtick"] = Keys.Oemtilde,
    };

    /// <summary>
    /// The low-level hook reports left/right variants of Shift, Ctrl and Alt,
    /// so the generic codes would never match.
    /// </summary>
    private static readonly Dictionary<Keys, string> Ambiguous = new()
    {
        [Keys.ShiftKey] = "LeftShift or RightShift",
        [Keys.Shift] = "LeftShift or RightShift",
        [Keys.ControlKey] = "LeftCtrl or RightCtrl",
        [Keys.Control] = "LeftCtrl or RightCtrl",
        [Keys.Menu] = "LeftAlt or RightAlt",
        [Keys.Alt] = "LeftAlt or RightAlt",
    };

    public static bool TryParse(string name, out int virtualKey, out string? error)
    {
        virtualKey = 0;
        error = null;
        name = name.Trim();

        if (name.Length == 0)
        {
            error = "key name is empty";
            return false;
        }

        // Keys is a [Flags] enum, so Enum.TryParse would OR "A, B" into an unrelated key.
        if (name.Contains(','))
        {
            error = $"\"{name}\" must be a single key";
            return false;
        }

        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            virtualKey = (int)Keys.D0 + (name[0] - '0');
            return true;
        }

        if (!Aliases.TryGetValue(name, out var key)
            && (int.TryParse(name, out _) || !Enum.TryParse(name, ignoreCase: true, out key)))
        {
            error = $"unknown key \"{name}\"";
            return false;
        }

        if (Ambiguous.TryGetValue(key, out var suggestion))
        {
            error = $"\"{name}\" is ambiguous, use {suggestion}";
            return false;
        }

        int code = (int)key;
        if (code is <= 0 or > 254)
        {
            error = $"\"{name}\" is not a single key";
            return false;
        }

        virtualKey = code;
        return true;
    }

    /// <summary>Stable, layout-independent name, as written to profile files.</summary>
    public static string Describe(int virtualKey) => ((Keys)virtualKey).ToString();

    private static readonly Dictionary<Keys, string> DisplayNames = new()
    {
        [Keys.Space] = "Space",
        [Keys.Enter] = "Enter",
        [Keys.Back] = "Backspace",
        [Keys.Tab] = "Tab",
        [Keys.Escape] = "Esc",
        [Keys.CapsLock] = "Caps Lock",
        [Keys.LShiftKey] = "Left Shift",
        [Keys.RShiftKey] = "Right Shift",
        [Keys.LControlKey] = "Left Ctrl",
        [Keys.RControlKey] = "Right Ctrl",
        [Keys.LMenu] = "Left Alt",
        [Keys.RMenu] = "Right Alt",
        [Keys.LWin] = "Left Win",
        [Keys.RWin] = "Right Win",
        [Keys.Apps] = "Menu",
        [Keys.Up] = "↑",
        [Keys.Down] = "↓",
        [Keys.Left] = "←",
        [Keys.Right] = "→",
        [Keys.PageUp] = "Page Up",
        [Keys.PageDown] = "Page Down",
        [Keys.Home] = "Home",
        [Keys.End] = "End",
        [Keys.Insert] = "Insert",
        [Keys.Delete] = "Delete",
        [Keys.PrintScreen] = "Print Screen",
        [Keys.Scroll] = "Scroll Lock",
        [Keys.Pause] = "Pause",
        [Keys.NumLock] = "Num Lock",
        [Keys.Multiply] = "Num *",
        [Keys.Add] = "Num +",
        [Keys.Subtract] = "Num -",
        [Keys.Divide] = "Num /",
        [Keys.Decimal] = "Num .",
    };

    /// <summary>Short label for the UI, using the character the current keyboard layout prints on the key.</summary>
    public static string Display(int virtualKey)
    {
        var key = (Keys)virtualKey;
        if (key is >= Keys.D0 and <= Keys.D9)
            return ((char)('0' + (virtualKey - (int)Keys.D0))).ToString();
        if (key is >= Keys.A and <= Keys.Z)
            return key.ToString();
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
            return $"Num {virtualKey - (int)Keys.NumPad0}";
        if (DisplayNames.TryGetValue(key, out var name))
            return name;

        if (key is (>= Keys.OemSemicolon and <= Keys.Oemtilde) or (>= Keys.OemOpenBrackets and <= Keys.OemBackslash))
        {
            uint ch = MapVirtualKeyEx((uint)virtualKey, MAPVK_VK_TO_CHAR, GetKeyboardLayout(0)) & 0x7FFF;
            if (ch > ' ')
                return char.ToUpperInvariant((char)ch).ToString();
        }

        return key.ToString();
    }

    private const uint MAPVK_VK_TO_CHAR = 2;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);
}
