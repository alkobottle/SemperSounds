using SemperSounds.Contracts;

namespace SemperSounds.Desktop.Core;

/// <summary>
/// Turns what is sounding into the one line the window shows.
/// </summary>
/// <remarks>
/// Clips overlap by design, so this has to cope with several at once. It names two and counts
/// the rest, because a strip that grows with the noise is the least readable thing on screen at
/// exactly the moment there is the most to read.
/// </remarks>
public static class NowPlayingSummary
{
    /// <summary>How many are named before the rest are merely counted.</summary>
    private const int NamedLimit = 2;

    public static string Describe(IReadOnlyList<NowPlaying> playing)
    {
        if (playing.Count == 0)
        {
            return string.Empty;
        }

        var named = string.Join(", ", playing.Take(NamedLimit).Select(Describe));

        return playing.Count <= NamedLimit ? named : $"{named} +{playing.Count - NamedLimit} more";
    }

    /// <summary>
    /// "Airhorn - alkobottle", or "Airhorn - alkobottle arrived" for an entry sound.
    /// </summary>
    /// <remarks>
    /// Entry sounds are marked as such deliberately. Nobody pressed anything, and reading it as
    /// somebody playing a clip at you attributes an action to a person who took none.
    /// </remarks>
    private static string Describe(NowPlaying playing) => playing.IsEntrySound
        ? $"{playing.SoundName} — {playing.PlayedBy} arrived"
        : $"{playing.SoundName} — {playing.PlayedBy}";
}
