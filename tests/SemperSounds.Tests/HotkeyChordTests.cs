using SemperSounds.Desktop.Core;

namespace SemperSounds.Tests;

/// <summary>
/// The key combination a binding is written down as.
/// </summary>
/// <remarks>
/// It round-trips through a config file on disk, so parsing and formatting have to agree
/// exactly. A disagreement does not throw — it silently produces a binding that never fires,
/// which from the user's chair is indistinguishable from the hook being broken.
/// </remarks>
public class HotkeyChordTests
{
    // Virtual-key codes, from the values Windows actually sends.
    private const int VkF1 = 0x70;
    private const int VkF13 = 0x7C;
    private const int VkA = 0x41;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;

    [Fact]
    public void ABareFunctionKey_RoundTrips()
    {
        var chord = new HotkeyChord(VkF1, HotkeyModifiers.None);

        Assert.Equal("F1", chord.ToString());
        Assert.Equal(chord, HotkeyChord.Parse("F1"));
    }

    [Theory]
    [InlineData(HotkeyModifiers.Control, "Ctrl+F1")]
    [InlineData(HotkeyModifiers.Alt, "Alt+F1")]
    [InlineData(HotkeyModifiers.Shift, "Shift+F1")]
    [InlineData(HotkeyModifiers.Windows, "Win+F1")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl+Shift+F1")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, "Ctrl+Alt+Shift+F1")]
    public void ModifiedChords_RoundTrip(HotkeyModifiers modifiers, string text)
    {
        var chord = new HotkeyChord(VkF1, modifiers);

        Assert.Equal(text, chord.ToString());
        Assert.Equal(chord, HotkeyChord.Parse(text));
    }

    [Fact]
    public void ModifierOrder_IsAlwaysTheSame()
    {
        // Written in any order, stored in one. Otherwise two config files describing the same
        // binding compare as different, and a saved chord can fail to match a captured one.
        Assert.Equal("Ctrl+Alt+Shift+F1", HotkeyChord.Parse("Shift+Alt+Ctrl+F1").ToString());
    }

    [Fact]
    public void Parsing_IgnoresSpacingAndCase()
    {
        Assert.Equal(HotkeyChord.Parse("Ctrl+Shift+F1"), HotkeyChord.Parse("  ctrl + SHIFT+f1 "));
    }

    [Fact]
    public void KeysAboveF12_AreSupported()
    {
        // The whole point of a macro keyboard: F13 to F24 exist on no game's keybind list.
        Assert.Equal("F13", new HotkeyChord(VkF13, HotkeyModifiers.None).ToString());
        Assert.Equal(new HotkeyChord(VkF13, HotkeyModifiers.None), HotkeyChord.Parse("F13"));
    }

    [Fact]
    public void LetterKeys_RoundTrip()
    {
        Assert.Equal("Ctrl+A", new HotkeyChord(VkA, HotkeyModifiers.Control).ToString());
    }

    [Fact]
    public void AModifierOnItsOwn_IsNotAChord()
    {
        // Captured while the user is still reaching for the real key. Accepting it would bind
        // Ctrl itself, which fires constantly and cannot be typed past.
        Assert.False(new HotkeyChord(VkControl, HotkeyModifiers.Control).IsValid);
        Assert.False(new HotkeyChord(VkShift, HotkeyModifiers.Shift).IsValid);
        Assert.True(new HotkeyChord(VkF1, HotkeyModifiers.Control).IsValid);
    }

    [Fact]
    public void AnEmptyChord_IsNotValid()
    {
        Assert.False(HotkeyChord.None.IsValid);
        Assert.Equal(string.Empty, HotkeyChord.None.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("+")]
    [InlineData("Ctrl+Nonsense")]
    [InlineData("Ctrl+Shift")]
    public void UnparseableText_BecomesNoneRatherThanThrowing(string? text)
    {
        // Reached by hand-editing the config file, which is a supported thing to do. A throw
        // here would take the whole app down on startup over one bad line.
        Assert.Equal(HotkeyChord.None, HotkeyChord.Parse(text));
    }

    [Fact]
    public void AKeyWithNoFriendlyName_StillRoundTrips()
    {
        // An exotic keyboard's extra buttons must survive the config file rather than being
        // silently dropped on the next save.
        var chord = new HotkeyChord(0xFF, HotkeyModifiers.Control);

        Assert.Equal("Ctrl+VKFF", chord.ToString());
        Assert.Equal(chord, HotkeyChord.Parse(chord.ToString()));
    }

    [Fact]
    public void TheSameKeyWithDifferentModifiers_IsADifferentChord()
    {
        Assert.NotEqual(new HotkeyChord(VkF1, HotkeyModifiers.None), new HotkeyChord(VkF1, HotkeyModifiers.Control));
    }
}
