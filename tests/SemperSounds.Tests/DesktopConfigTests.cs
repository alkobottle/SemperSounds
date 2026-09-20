using SemperSounds.Contracts;
using SemperSounds.Desktop.Core;

namespace SemperSounds.Tests;

/// <summary>
/// The config file and the auto-summon rule: the two pieces of the desktop client that decide
/// what a key press does, both reachable without a window or a keyboard.
/// </summary>
public class DesktopConfigTests
{
    private static readonly Guid Airhorn = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static DesktopConfig Populated() => new()
    {
        ServerUrl = "https://sounds.example",
        Bindings = [new HotkeyBinding { SoundId = Airhorn, Chord = HotkeyChord.Parse("Ctrl+F1"), CachedName = "Airhorn" }],
        StopAllChord = HotkeyChord.Parse("F8"),
        AutoSummon = false,
        ShowOverlay = true,
        RunAtStartup = true,
    };

    [Fact]
    public void AConfig_SurvivesARoundTrip()
    {
        var restored = DesktopConfig.Deserialize(Populated().Serialize());

        Assert.Equal("https://sounds.example", restored.ServerUrl);
        Assert.Equal(HotkeyChord.Parse("Ctrl+F1"), Assert.Single(restored.Bindings).Chord);
        Assert.Equal("Airhorn", restored.Bindings[0].CachedName);
        Assert.Equal(Airhorn, restored.Bindings[0].SoundId);
        Assert.Equal(HotkeyChord.Parse("F8"), restored.StopAllChord);
        Assert.False(restored.AutoSummon);
        Assert.True(restored.ShowOverlay);
        Assert.True(restored.RunAtStartup);
    }

    [Fact]
    public void ChordsAreStoredAsText_SoTheFileCanBeEditedByHand()
    {
        Assert.Contains("\"chord\": \"Ctrl+F1\"", Populated().Serialize());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("{ not json")]
    [InlineData("{\"bindings\": \"should have been an array\"}")]
    [InlineData("[]")]
    public void AnUnreadableFile_FallsBackToDefaults(string? json)
    {
        // Half-written by a crash, or mangled by hand. Losing the settings is acceptable;
        // refusing to start is not.
        var config = DesktopConfig.Deserialize(json);

        Assert.Empty(config.Bindings);
        Assert.True(config.AutoSummon);
    }

    [Fact]
    public void AnUnparseableChord_BecomesUnboundRatherThanFailingTheWholeFile()
    {
        var config = DesktopConfig.Deserialize(
            """{"bindings":[{"soundId":"11111111-1111-1111-1111-111111111111","chord":"Ctrl+Nonsense","cachedName":"Airhorn"}]}""");

        Assert.Equal(HotkeyChord.None, Assert.Single(config.Bindings).Chord);
        Assert.Equal("Airhorn", config.Bindings[0].CachedName);
    }

    [Fact]
    public void AFileFromANewerBuild_KeepsWhatItUnderstands()
    {
        // Same tolerance BoardPreferencesJson has, for the same reason: a newer build's file
        // must not wipe the settings when an older one reads it.
        var config = DesktopConfig.Deserialize(
            """{"serverUrl":"https://sounds.example","somethingFromTheFuture":{"a":1}}""");

        Assert.Equal("https://sounds.example", config.ServerUrl);
    }

    [Fact]
    public void OverlayAndAudioCue_AreOffByDefault()
    {
        // The overlay cannot draw over an exclusive-fullscreen game and a window that takes
        // focus would minimise one, so neither is on until the user has seen it behave.
        var config = new DesktopConfig();

        Assert.False(config.ShowOverlay);
        Assert.False(config.PlayAudioCue);
    }

    [Theory]
    [InlineData(PlayFailure.BotAbsent, true)]
    [InlineData(PlayFailure.WrongChannel, true)]
    [InlineData(PlayFailure.NotInVoice, false)]
    [InlineData(PlayFailure.Cooldown, false)]
    [InlineData(PlayFailure.Missing, false)]
    [InlineData(PlayFailure.AlreadyPlaying, false)]
    [InlineData(PlayFailure.Other, false)]
    [InlineData(PlayFailure.None, false)]
    public void AutoSummon_OnlyRetriesWhatAJoinCouldFix(PlayFailure failure, bool expected) =>
        Assert.Equal(expected, AutoSummonPolicy.ShouldSummon(failure, enabled: true));

    [Fact]
    public void AutoSummonDisabled_NeverSummons()
    {
        Assert.False(AutoSummonPolicy.ShouldSummon(PlayFailure.BotAbsent, enabled: false));
    }

    [Fact]
    public void FailuresAreReported_ExceptTheOneThatIsAudible()
    {
        // Including the cooldown. A press that does nothing and says nothing looks exactly
        // like the keyboard hook having died, which is a real failure mode here.
        Assert.True(AutoSummonPolicy.ShouldReport(PlayFailure.Cooldown));
        Assert.True(AutoSummonPolicy.ShouldReport(PlayFailure.Missing));
        Assert.False(AutoSummonPolicy.ShouldReport(PlayFailure.None));

        // The exception. A clip that is still playing is announcing itself out loud already,
        // so a cue on every press of a held key would be the very noise this prevents.
        Assert.False(AutoSummonPolicy.ShouldReport(PlayFailure.AlreadyPlaying));
    }
}
