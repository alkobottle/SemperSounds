namespace SemperSounds.Desktop.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// One key combination, as captured from the keyboard and as written to the config file.
/// </summary>
/// <remarks>
/// <para>
/// Held as a raw virtual-key code rather than an enum of every key Windows has. The hook hands
/// back a number; storing anything else would mean a lookup table that has to be complete to
/// be correct, and would quietly drop whatever it had not heard of — including exactly the
/// F13-to-F24 range a macro keyboard sends, which is the most useful thing to bind here
/// because no game claims it.
/// </para>
/// <para>
/// Formatting is canonical and parsing is forgiving. The text goes through a file a person may
/// reasonably edit by hand, so "shift+alt+ctrl+f1" has to mean the same chord as the one that
/// was written out — while what gets written back is always in one order, or two config files
/// describing the same binding would not compare equal.
/// </para>
/// </remarks>
public readonly record struct HotkeyChord(int VirtualKey, HotkeyModifiers Modifiers)
{
    public static HotkeyChord None { get; } = new(0, HotkeyModifiers.None);

    /// <summary>
    /// False for an unset chord and for one whose key is itself a modifier.
    /// </summary>
    /// <remarks>
    /// The modifier case is not theoretical: capture reads the keyboard while the user is
    /// still reaching for the real key, so Ctrl alone is what it sees first. Binding that
    /// would fire on every use of Ctrl anywhere, and the user could not type their way out of
    /// it.
    /// </remarks>
    public bool IsValid => VirtualKey != 0 && !IsModifierKey(VirtualKey);

    public override string ToString()
    {
        if (VirtualKey == 0)
        {
            return string.Empty;
        }

        // One fixed order, always. Equality is structural, so two spellings of one chord must
        // not be able to reach disk.
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));

        return string.Join('+', parts);
    }

    /// <summary>
    /// Reads a chord back. Returns <see cref="None"/> for anything unrecognisable rather than
    /// throwing — the text comes from a file a person may have edited, and one bad line must
    /// not stop the app starting.
    /// </summary>
    public static HotkeyChord Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        var modifiers = HotkeyModifiers.None;
        var key = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= HotkeyModifiers.Control; break;
                case "ALT": modifiers |= HotkeyModifiers.Alt; break;
                case "SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                case "WIN" or "WINDOWS": modifiers |= HotkeyModifiers.Windows; break;
                default:
                    // A second non-modifier token means the text is malformed, not that the
                    // last one wins — silently keeping one of two keys is how a typo turns
                    // into a binding that fires on the wrong press.
                    if (key != 0 || !TryParseKey(raw, out key))
                    {
                        return None;
                    }

                    break;
            }
        }

        var chord = new HotkeyChord(key, modifiers);
        return chord.IsValid ? chord : None;
    }

    private static bool TryParseKey(string name, out int virtualKey)
    {
        virtualKey = 0;
        var upper = name.ToUpperInvariant();

        // F1 to F24 are contiguous from 0x70, which is what makes the macro-keyboard range
        // work without a table.
        if (upper.Length >= 2 && upper[0] == 'F' &&
            int.TryParse(upper[1..], out var number) && number is >= 1 and <= 24)
        {
            virtualKey = 0x70 + number - 1;
            return true;
        }

        if (upper.Length == 1 && upper[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            virtualKey = upper[0];
            return true;
        }

        // The escape hatch that makes KeyName's fallback honest: any key with no friendly name
        // is written as VK<hex> and read straight back, so an exotic keyboard's extra buttons
        // survive a round trip through the config file instead of being silently dropped.
        if (upper.StartsWith("VK", StringComparison.Ordinal) &&
            int.TryParse(upper[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var code) &&
            code is > 0 and <= 0xFF)
        {
            virtualKey = code;
            return true;
        }

        return NamedKeys.TryGetValue(upper, out virtualKey);
    }

    private static string KeyName(int virtualKey)
    {
        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x70 + 1}";
        }

        if (virtualKey is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)virtualKey).ToString();
        }

        foreach (var (name, code) in NamedKeys)
        {
            if (code == virtualKey)
            {
                return name;
            }
        }

        // Nothing is lost: TryParseKey reads this spelling straight back.
        return $"VK{virtualKey:X2}";
    }

    /// <summary>
    /// True for Shift, Control, Alt and the Windows keys, in both their generic and
    /// side-specific forms.
    /// </summary>
    /// <remarks>
    /// Public because capture needs it. Somebody assigning Ctrl+F1 presses Ctrl first, and a
    /// capture that took the first key it saw would take that one - producing an invalid chord
    /// and eating the attempt, so combos could never be assigned at all.
    /// </remarks>
    public static bool IsModifier(int virtualKey) => IsModifierKey(virtualKey);

    private static bool IsModifierKey(int virtualKey) => virtualKey
        is 0x10 or 0xA0 or 0xA1   // Shift, LShift, RShift
        or 0x11 or 0xA2 or 0xA3   // Control, LControl, RControl
        or 0x12 or 0xA4 or 0xA5   // Alt, LAlt, RAlt
        or 0x5B or 0x5C;          // LWin, RWin

    /// <summary>The handful of non-alphanumeric keys worth naming. Anything else keeps its code.</summary>
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPACE"] = 0x20,
        ["INSERT"] = 0x2D,
        ["DELETE"] = 0x2E,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PAGEUP"] = 0x21,
        ["PAGEDOWN"] = 0x22,
        ["UP"] = 0x26,
        ["DOWN"] = 0x28,
        ["LEFT"] = 0x25,
        ["RIGHT"] = 0x27,
        ["NUMPAD0"] = 0x60,
        ["NUMPAD1"] = 0x61,
        ["NUMPAD2"] = 0x62,
        ["NUMPAD3"] = 0x63,
        ["NUMPAD4"] = 0x64,
        ["NUMPAD5"] = 0x65,
        ["NUMPAD6"] = 0x66,
        ["NUMPAD7"] = 0x67,
        ["NUMPAD8"] = 0x68,
        ["NUMPAD9"] = 0x69,
    };
}
