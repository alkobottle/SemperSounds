using SemperSounds.Desktop.Core;

namespace SemperSounds.Tests;

/// <summary>
/// Stops the client asking for a clip it already knows is playing.
/// </summary>
/// <remarks>
/// <para>
/// The server refuses a re-trigger too, and that is the authoritative check — it has the mixer
/// and it speaks for every client at once. This one exists because the round trip is pure
/// waste: a held-down or hammered key would otherwise fire a hub call per press, all of them
/// to be told no, over a connection the hotkey path wants kept quiet and fast.
/// </para>
/// <para>
/// It is deliberately a duration timer rather than a flag cleared by the server, because
/// nothing tells the client when a clip ended. Being approximately right and cheap beats being
/// exactly right and chatty.
/// </para>
/// </remarks>
public class RetriggerGuardTests
{
    private static readonly Guid Airhorn = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bruh = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

    private static (RetriggerGuard Guard, FakeTimeProvider Time) New()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        return (new RetriggerGuard(time), time);
    }

    [Fact]
    public void TheFirstPress_IsAllowed()
    {
        var (guard, _) = New();

        Assert.True(guard.TryBegin(Airhorn, TwoSeconds));
    }

    [Fact]
    public void ASecondPress_WhileStillPlaying_IsRefused()
    {
        var (guard, time) = New();
        guard.TryBegin(Airhorn, TwoSeconds);

        time.Advance(TimeSpan.FromMilliseconds(500));

        Assert.False(guard.TryBegin(Airhorn, TwoSeconds));
    }

    [Fact]
    public void OnceTheClipHasEnded_ItCanPlayAgain()
    {
        var (guard, time) = New();
        guard.TryBegin(Airhorn, TwoSeconds);

        time.Advance(TwoSeconds);

        Assert.True(guard.TryBegin(Airhorn, TwoSeconds));
    }

    [Fact]
    public void ADifferentSound_IsUnaffected()
    {
        // The guard is per clip. Two keys in quick succession is ordinary use, not hammering.
        var (guard, _) = New();
        guard.TryBegin(Airhorn, TwoSeconds);

        Assert.True(guard.TryBegin(Bruh, TwoSeconds));
    }

    [Fact]
    public void ARefusedPress_DoesNotExtendTheBlock()
    {
        // Otherwise leaning on a key would hold the clip blocked for as long as the key is
        // held, and it would never become playable again.
        var (guard, time) = New();
        guard.TryBegin(Airhorn, TwoSeconds);

        time.Advance(TimeSpan.FromMilliseconds(1900));
        Assert.False(guard.TryBegin(Airhorn, TwoSeconds));

        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(guard.TryBegin(Airhorn, TwoSeconds));
    }

    [Fact]
    public void Clearing_LetsItPlayImmediately()
    {
        // Used when the play was refused after all: the clip never started, so nothing should
        // be waiting on it to finish.
        var (guard, _) = New();
        guard.TryBegin(Airhorn, TwoSeconds);

        guard.Clear(Airhorn);

        Assert.True(guard.TryBegin(Airhorn, TwoSeconds));
    }

    [Fact]
    public void AnUnknownDuration_StillGuardsBriefly()
    {
        // The library may not have loaded yet. Falling through with no guard at all would make
        // the very first press of a session the one case hammering still works on.
        var (guard, time) = New();

        Assert.True(guard.TryBegin(Airhorn, duration: null));
        Assert.False(guard.TryBegin(Airhorn, duration: null));

        time.Advance(RetriggerGuard.UnknownDurationGuard);
        Assert.True(guard.TryBegin(Airhorn, duration: null));
    }

    [Fact]
    public void AZeroOrNegativeDuration_IsTreatedAsUnknown()
    {
        var (guard, _) = New();

        Assert.True(guard.TryBegin(Airhorn, TimeSpan.Zero));
        Assert.False(guard.TryBegin(Airhorn, TimeSpan.Zero));
    }

    [Fact]
    public void Reset_UnblocksEverything()
    {
        // Pressing the panic key stops the clips, so nothing should still be held back by a
        // timer counting down for audio that is no longer playing.
        var (guard, _) = New();
        guard.TryBegin(Airhorn, TwoSeconds);
        guard.TryBegin(Bruh, TwoSeconds);

        guard.Reset();

        Assert.True(guard.TryBegin(Airhorn, TwoSeconds));
        Assert.True(guard.TryBegin(Bruh, TwoSeconds));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
