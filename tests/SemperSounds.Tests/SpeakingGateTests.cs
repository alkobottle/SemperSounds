using SemperSounds.Core.Audio;

namespace SemperSounds.Tests;

public class SpeakingGateTests
{
    private static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(400);

    private static (SpeakingGate Gate, FakeTimeProvider Time) Create(bool speaking = true)
    {
        var time = new FakeTimeProvider();
        return (new SpeakingGate(Linger, time) { IsSpeaking = speaking }, time);
    }

    /// <summary>Lets the tests advance time without sleeping.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    [Fact]
    public void AudioWhileSilent_RaisesSpeaking()
    {
        var (gate, _) = Create(speaking: false);

        Assert.True(gate.Update(hasAudio: true));
        Assert.True(gate.IsSpeaking);
    }

    [Fact]
    public void ContinuedAudio_DoesNotResendTheFlag()
    {
        // The speaking payload is rate-limited, so an unchanged state must stay quiet.
        var (gate, _) = Create(speaking: false);
        gate.Update(hasAudio: true);

        Assert.Null(gate.Update(hasAudio: true));
        Assert.Null(gate.Update(hasAudio: true));
    }

    [Fact]
    public void BriefSilence_KeepsSpeakingRaised()
    {
        var (gate, time) = Create();

        Assert.Null(gate.Update(hasAudio: false));
        time.Advance(TimeSpan.FromMilliseconds(200));

        Assert.Null(gate.Update(hasAudio: false));
        Assert.True(gate.IsSpeaking);
    }

    [Fact]
    public void SilenceBeyondTheLinger_LowersSpeaking()
    {
        var (gate, time) = Create();
        gate.Update(hasAudio: false);

        time.Advance(Linger + TimeSpan.FromMilliseconds(1));

        Assert.False(gate.Update(hasAudio: false));
        Assert.False(gate.IsSpeaking);
    }

    [Fact]
    public void AudioResumingWithinTheLinger_NeverTogglesTheFlag()
    {
        // Rapid-fire sounds leave gaps of silence between clips. Toggling across each gap
        // would spam the voice websocket for no visible benefit.
        var (gate, time) = Create();

        gate.Update(hasAudio: false);
        time.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Null(gate.Update(hasAudio: true));
        Assert.True(gate.IsSpeaking);

        // And the silence timer restarted, so the next gap gets a fresh linger.
        gate.Update(hasAudio: false);
        time.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Null(gate.Update(hasAudio: false));
    }

    [Fact]
    public void AlreadyLowered_StaysQuietWhileSilent()
    {
        var (gate, time) = Create(speaking: false);

        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Null(gate.Update(hasAudio: false));
    }
}
