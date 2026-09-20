using SemperSounds.Desktop.Core;

namespace SemperSounds.Tests;

/// <summary>
/// Turns a key press into what the app should do about it.
/// </summary>
/// <remarks>
/// Pure, and separate from the hook, because the hook cannot be unit tested and this is where
/// every rule that could be wrong lives. The mute rule especially: it is the thing standing
/// between the user and firing a clip into voice while typing in a game's chat box.
/// </remarks>
public class HotkeyRouterTests
{
    private static readonly Guid Airhorn = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bruh = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly HotkeyChord F1 = HotkeyChord.Parse("F1");
    private static readonly HotkeyChord F2 = HotkeyChord.Parse("F2");
    private static readonly HotkeyChord F3 = HotkeyChord.Parse("F3");
    private static readonly HotkeyChord F4 = HotkeyChord.Parse("F4");
    private static readonly HotkeyChord F5 = HotkeyChord.Parse("F5");
    private static readonly HotkeyChord Unbound = HotkeyChord.Parse("Ctrl+Alt+F12");

    private static DesktopConfig Config() => new()
    {
        Bindings =
        [
            new HotkeyBinding { SoundId = Airhorn, Chord = F1, CachedName = "Airhorn" },
            new HotkeyBinding { SoundId = Bruh, Chord = F2, CachedName = "Bruh" },
        ],
        StopAllChord = F3,
        SummonChord = F4,
        MuteChord = F5,
    };

    private static HotkeyRouter New() => new(Config());

    [Fact]
    public void ABoundChord_PlaysItsSound()
    {
        var action = New().Route(F1);

        Assert.Equal(HotkeyActionKind.Play, action.Kind);
        Assert.Equal(Airhorn, action.SoundId);
        Assert.Equal("Airhorn", action.SoundName);
    }

    [Fact]
    public void EachBinding_KeepsItsOwnSound()
    {
        Assert.Equal(Bruh, New().Route(F2).SoundId);
    }

    [Fact]
    public void AnUnboundChord_DoesNothing()
    {
        // The hook sees every key on the keyboard, so this is the overwhelmingly common case
        // and has to be the cheap one.
        Assert.Equal(HotkeyActionKind.None, New().Route(Unbound).Kind);
    }

    [Theory]
    [InlineData("F3", HotkeyActionKind.StopAll)]
    [InlineData("F4", HotkeyActionKind.ToggleSummon)]
    [InlineData("F5", HotkeyActionKind.ToggleMute)]
    public void TheSpecialChords_RouteToTheirActions(string chord, HotkeyActionKind expected) =>
        Assert.Equal(expected, New().Route(HotkeyChord.Parse(chord)).Kind);

    [Fact]
    public void WhileMuted_NothingPlays()
    {
        // The reason mute exists: typing in a game's chat box must not fire clips into voice.
        var router = New();
        router.Route(F5);

        Assert.Equal(HotkeyActionKind.None, router.Route(F1).Kind);
        Assert.Equal(HotkeyActionKind.None, router.Route(F3).Kind);
        Assert.Equal(HotkeyActionKind.None, router.Route(F4).Kind);
    }

    [Fact]
    public void TheMuteChord_StillWorksWhileMuted()
    {
        // Otherwise muting is a one-way door and the only way out is the tray menu.
        var router = New();
        router.Route(F5);

        Assert.Equal(HotkeyActionKind.ToggleMute, router.Route(F5).Kind);
        Assert.False(router.IsMuted);
    }

    [Fact]
    public void Muting_IsReportedSoTheTrayCanShowIt()
    {
        var router = New();
        Assert.False(router.IsMuted);

        router.Route(F5);
        Assert.True(router.IsMuted);
    }

    [Fact]
    public void AnUnsetSpecialChord_MatchesNothing()
    {
        // All three are unbound by default. Routing HotkeyChord.None must not mean "every
        // unrecognised key stops all sounds".
        var router = new HotkeyRouter(new DesktopConfig());

        Assert.Equal(HotkeyActionKind.None, router.Route(HotkeyChord.None).Kind);
        Assert.Equal(HotkeyActionKind.None, router.Route(F1).Kind);
    }

    [Fact]
    public void ABindingWithNoChord_IsIgnored()
    {
        // Every sound the user has not bound yet is stored this way.
        var router = new HotkeyRouter(new DesktopConfig
        {
            Bindings = [new HotkeyBinding { SoundId = Airhorn, Chord = HotkeyChord.None, CachedName = "Airhorn" }],
        });

        Assert.Equal(HotkeyActionKind.None, router.Route(HotkeyChord.None).Kind);
    }

    [Fact]
    public void RebindingLive_TakesEffectWithoutARestart()
    {
        var router = New();

        router.Update(new DesktopConfig
        {
            Bindings = [new HotkeyBinding { SoundId = Bruh, Chord = F1, CachedName = "Bruh" }],
        });

        Assert.Equal(Bruh, router.Route(F1).SoundId);
    }

    [Fact]
    public void Rebinding_DoesNotSilentlyUnmute()
    {
        // Saving settings while muted must not start firing clips again.
        var router = New();
        router.Route(F5);

        router.Update(Config());

        Assert.True(router.IsMuted);
    }

    [Fact]
    public void TheChordsTheHookMustWatch_AreExactlyTheBoundOnes()
    {
        // The hook callback runs on every keystroke and has to return well inside Windows'
        // timeout, so it matches against this set rather than walking the config.
        var watched = New().WatchedChords;

        Assert.Equal(5, watched.Count);
        Assert.Contains(F1, watched);
        Assert.Contains(F5, watched);
        Assert.DoesNotContain(HotkeyChord.None, watched);
    }
}
