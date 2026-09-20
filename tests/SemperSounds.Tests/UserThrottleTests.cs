using SemperSounds.Web.Services;

namespace SemperSounds.Tests;

/// <summary>
/// Paces how often one person may move the bot between channels.
/// </summary>
/// <remarks>
/// Playing already has a cooldown; joining and leaving never did, because until there was a
/// desktop client the only way to ask was to click a button, and a person cannot click fast
/// enough to matter. A key can. Hopping the bot between channels in a loop means a voice
/// reconnect per hop, which ends with Discord rate-limiting or dropping the gateway — and
/// that surfaces as "the bot randomly stopped working", nowhere near the cause.
/// </remarks>
public class UserThrottleTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private static (UserThrottle Throttle, FakeTimeProvider Time) New()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        return (new UserThrottle(Window, time), time);
    }

    [Fact]
    public void TheFirstAttempt_IsAllowed()
    {
        var (throttle, _) = New();

        Assert.True(throttle.TryAcquire(1, out _));
    }

    [Fact]
    public void AnImmediateSecondAttempt_IsRefused()
    {
        var (throttle, _) = New();
        throttle.TryAcquire(1, out _);

        Assert.False(throttle.TryAcquire(1, out var remaining));
        Assert.Equal(Window, remaining);
    }

    [Fact]
    public void AfterTheWindow_ItIsAllowedAgain()
    {
        var (throttle, time) = New();
        throttle.TryAcquire(1, out _);

        time.Advance(Window);

        Assert.True(throttle.TryAcquire(1, out _));
    }

    [Fact]
    public void RemainingTime_CountsDown()
    {
        var (throttle, time) = New();
        throttle.TryAcquire(1, out _);

        time.Advance(TimeSpan.FromMilliseconds(500));

        Assert.False(throttle.TryAcquire(1, out var remaining));
        Assert.Equal(TimeSpan.FromMilliseconds(1500), remaining);
    }

    [Fact]
    public void OnePersonsThrottle_DoesNotPaceAnother()
    {
        // Two people in different channels both summoning is ordinary use, not abuse.
        var (throttle, _) = New();
        throttle.TryAcquire(1, out _);

        Assert.True(throttle.TryAcquire(2, out _));
    }

    [Fact]
    public void ARefusedAttempt_DoesNotExtendTheWindow()
    {
        // Otherwise holding a key down locks the user out for as long as they hold it, and
        // the wait never counts down.
        var (throttle, time) = New();
        throttle.TryAcquire(1, out _);

        time.Advance(TimeSpan.FromMilliseconds(1900));
        Assert.False(throttle.TryAcquire(1, out _));

        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(throttle.TryAcquire(1, out _));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
