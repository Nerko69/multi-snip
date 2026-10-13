namespace MultiSnip;

/// <summary>Helpers for the global snip hotkey, stored as a <see cref="Keys"/> value (key code + modifiers).</summary>
internal static class Hotkey
{
    public const Keys Default = Keys.Control | Keys.Shift | Keys.S;

    /// <summary>Shortcuts Multi Snip uses inside its own window; a global hotkey would swallow them.</summary>
    private static readonly Keys[] InAppShortcuts =
    {
        Keys.Control | Keys.N, Keys.Control | Keys.Shift | Keys.N, Keys.Control | Keys.Shift | Keys.R, Keys.Control | Keys.O, Keys.Control | Keys.S,
        Keys.Control | Keys.V, Keys.Control | Keys.C, Keys.Control | Keys.Up, Keys.Control | Keys.Down,
    };

    public static string Format(Keys keys)
    {
        if (keys == Keys.None) return "None";
        var parts = new List<string>();
        if (keys.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (keys.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (keys.HasFlag(Keys.Shift)) parts.Add("Shift");
        Keys code = keys & Keys.KeyCode;
        if (code != Keys.None && !IsModifierKey(code)) parts.Add(KeyName(code));
        return string.Join("+", parts);
    }

    public static uint Modifiers(Keys keys)
    {
        uint m = NativeMethods.MOD_NOREPEAT;
        if (keys.HasFlag(Keys.Control)) m |= NativeMethods.MOD_CONTROL;
        if (keys.HasFlag(Keys.Alt)) m |= NativeMethods.MOD_ALT;
        if (keys.HasFlag(Keys.Shift)) m |= NativeMethods.MOD_SHIFT;
        return m;
    }

    public static bool IsModifierKey(Keys code) =>
        code is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LControlKey or Keys.RControlKey
            or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;

    /// <summary>Returns null when the combination is usable, otherwise the reason it is not.</summary>
    public static string? Validate(Keys keys)
    {
        if (keys == Keys.None) return null; // disabled
        Keys code = keys & Keys.KeyCode;
        if (code == Keys.None || IsModifierKey(code))
            return "Press a key together with the modifiers, e.g. Ctrl+Shift+S.";
        bool hasCtrlOrAlt = keys.HasFlag(Keys.Control) || keys.HasFlag(Keys.Alt);
        bool standalone = code is >= Keys.F1 and <= Keys.F24 or Keys.PrintScreen or Keys.Pause or Keys.Scroll;
        if (!hasCtrlOrAlt && !standalone)
            return "Use Ctrl and/or Alt with this key (or pick a function key, Print Screen, Pause or Scroll Lock), " +
                   "otherwise normal typing would trigger a snip.";
        if (InAppShortcuts.Contains(keys))
            return $"{Format(keys)} is already a Multi Snip shortcut inside the app. Please choose another combination.";
        return null;
    }

    private static string KeyName(Keys code) => code switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (code - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (code - Keys.NumPad0),
        Keys.PrintScreen => "PrintScreen",
        Keys.Scroll => "ScrollLock",
        Keys.Next => "PageDown",
        Keys.Prior => "PageUp",
        Keys.Oemtilde => "`",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemPipe => "\\",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        _ => code.ToString(),
    };
}
