namespace StarryDisplay.Core;

/// <summary>Global hotkey preferences. An empty string turns that one hotkey off.</summary>
public sealed class HotkeySettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Click-through on/off for whatever is showing (the original Ctrl+Alt+L).</summary>
    public string ClickThrough { get; set; } = "Ctrl+Alt+L";
    /// <summary>Desktop display ↔ gaming overlay.</summary>
    public string Presentation { get; set; } = "Ctrl+Alt+O";
    /// <summary>Show/hide whatever is the current presentation.</summary>
    public string Visibility { get; set; } = "Ctrl+Alt+H";

    public IEnumerable<(string Action, string Gesture)> All()
    {
        yield return (HotkeyActions.ClickThrough, ClickThrough);
        yield return (HotkeyActions.Presentation, Presentation);
        yield return (HotkeyActions.Visibility, Visibility);
    }

    public string Get(string action) => action switch
    {
        HotkeyActions.ClickThrough => ClickThrough,
        HotkeyActions.Presentation => Presentation,
        HotkeyActions.Visibility => Visibility,
        _ => ""
    };

    public void Set(string action, string gesture)
    {
        switch (action)
        {
            case HotkeyActions.ClickThrough: ClickThrough = gesture; break;
            case HotkeyActions.Presentation: Presentation = gesture; break;
            case HotkeyActions.Visibility: Visibility = gesture; break;
        }
    }
}

/// <summary>Hotkey actions; the values are also the app's command strings.</summary>
public static class HotkeyActions
{
    public const string ClickThrough = "lock";
    public const string Presentation = "overlay";
    public const string Visibility = "toggle-quiet";
}

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Ctrl = 0x0002,
    Shift = 0x0004,
    Win = 0x0008
}

/// <summary>
/// A key combination for RegisterHotKey, parsed from and printed as "Ctrl+Alt+O". UI-free so it is unit-tested.
///
/// Rules (to stay out of a game's way):
/// - At least Ctrl, Alt or Win is required. Plain keys and Shift+key are refused, because a registered hotkey is
///   swallowed system-wide and the game would never see that key.
/// - Only keys games rarely bind with those modifiers: letters, digits, F1–F24, and a few navigation keys.
/// </summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["End"] = 0x23, ["Home"] = 0x24,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Pause"] = 0x13, ["ScrollLock"] = 0x91,
        ["Num0"] = 0x60, ["Num1"] = 0x61, ["Num2"] = 0x62, ["Num3"] = 0x63, ["Num4"] = 0x64,
        ["Num5"] = 0x65, ["Num6"] = 0x66, ["Num7"] = 0x67, ["Num8"] = 0x68, ["Num9"] = 0x69,
    };

    /// <summary>Parses "Ctrl+Alt+O" (any order, case-insensitive; "Control" and "Windows" accepted). Null if invalid or not allowed.</summary>
    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mods = HotkeyModifiers.None;
        uint? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; continue;
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win" or "windows": mods |= HotkeyModifiers.Win; continue;
            }
            if (key is not null) return null; // two non-modifier keys
            key = KeyFromName(raw);
            if (key is null) return null;
        }
        return key is { } vk ? Create(mods, vk) : null;
    }

    /// <summary>From a pressed key (e.g. in the Settings recorder). Null if the combination isn't allowed.</summary>
    public static HotkeyGesture? Create(HotkeyModifiers modifiers, uint virtualKey)
    {
        if ((modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0) return null;
        return NameFromKey(virtualKey) is null ? null : new HotkeyGesture(modifiers, virtualKey);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(NameFromKey(VirtualKey) ?? $"0x{VirtualKey:X2}");
        return string.Join("+", parts);
    }

    private static uint? KeyFromName(string name)
    {
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) return char.ToUpperInvariant(name[0]);
        if (name.Length is >= 2 and <= 3 && (name[0] is 'F' or 'f') && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24) return (uint)(0x70 + f - 1);
        return Named.TryGetValue(name, out var vk) ? vk : null;
    }

    private static string? NameFromKey(uint vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x70 + 1}";
        foreach (var (name, value) in Named) if (value == vk) return name;
        return null;
    }
}
