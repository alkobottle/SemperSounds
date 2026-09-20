using SemperSounds.Contracts;
using SemperSounds.Desktop.Core;

namespace SemperSounds.Tests;

/// <summary>
/// The single line the window shows while clips are sounding.
/// </summary>
/// <remarks>
/// Clips overlap by design — that is what the mixer is for — so this has to read well when
/// several are going at once, which is precisely when there is least room and most to say.
/// </remarks>
public class NowPlayingSummaryTests
{
    private static NowPlaying Played(string sound, string by) => new(Guid.NewGuid(), sound, by, IsEntrySound: false);

    private static NowPlaying Entry(string sound, string by) => new(Guid.NewGuid(), sound, by, IsEntrySound: true);

    [Fact]
    public void NothingPlaying_IsAnEmptyLine()
    {
        // The strip binds its visibility to this being non-empty, so a quiet channel has to
        // produce nothing at all rather than "nothing is playing".
        Assert.Equal(string.Empty, NowPlayingSummary.Describe([]));
    }

    [Fact]
    public void OneClip_NamesTheSoundAndWhoPlayedIt()
    {
        Assert.Equal("Airhorn — alkobottle", NowPlayingSummary.Describe([Played("Airhorn", "alkobottle")]));
    }

    [Fact]
    public void TwoClips_AreBothNamed()
    {
        var line = NowPlayingSummary.Describe([Played("Airhorn", "alkobottle"), Played("Bruh", "mace")]);

        Assert.Equal("Airhorn — alkobottle, Bruh — mace", line);
    }

    [Fact]
    public void BeyondTwo_TheRestAreCounted()
    {
        // A line that grows with the noise is unreadable exactly when it matters most.
        var line = NowPlayingSummary.Describe(
            [Played("Airhorn", "alkobottle"), Played("Bruh", "mace"), Played("Cantina", "malimo")]);

        Assert.Equal("Airhorn — alkobottle, Bruh — mace +1 more", line);
    }

    [Fact]
    public void ManyClips_CountTheRemainderCorrectly()
    {
        var line = NowPlayingSummary.Describe(
            [.. Enumerable.Range(0, 7).Select(i => Played($"Sound {i}", "alkobottle"))]);

        Assert.EndsWith("+5 more", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntrySound_SaysNobodyPressedAnything()
    {
        // Attributing it as "malimo played Cantina" would credit an action to somebody who
        // only walked into the channel.
        Assert.Equal("Cantina — malimo arrived", NowPlayingSummary.Describe([Entry("Cantina", "malimo")]));
    }

    [Fact]
    public void AMixOfBoth_KeepsEachKindDistinct()
    {
        var line = NowPlayingSummary.Describe([Entry("Cantina", "malimo"), Played("Airhorn", "alkobottle")]);

        Assert.Equal("Cantina — malimo arrived, Airhorn — alkobottle", line);
    }
}
