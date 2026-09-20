namespace SemperSounds.Desktop.ViewModels;

/// <summary>
/// The Font Awesome codepoints this app draws.
/// </summary>
/// <remarks>
/// Named constants rather than literals at the point of use, because a codepoint is unreadable
/// and a wrong one does not fail - it renders as an empty box, which looks like a font that did
/// not load rather than like a typo. All are Font Awesome 6 Free Solid; see
/// Assets/FONT-AWESOME-LICENSE.txt.
/// </remarks>
public static class Glyphs
{
    /// <summary>Headphones: preview on this machine, as opposed to playing into voice.</summary>
    public const string Headphones = "";

    public const string Stop = "";

    /// <summary>A speaker, for playing into the channel where other people hear it.</summary>
    public const string VolumeHigh = "";

    public const string XMark = "";
}
