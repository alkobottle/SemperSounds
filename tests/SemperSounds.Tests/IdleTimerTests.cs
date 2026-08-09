using SemperSounds.Core.Audio;

namespace SemperSounds.Tests;

public class IdleTimerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static (IdleTimer Timer, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider();
        return (new IdleTimer(Timeout, time), time);
    }

    [Fact]
    public void WhileSomeoneIsListening_ItNeverLeaves()
    {
        var (timer, time) = Create();

        time.Advance(TimeSpan.FromHours(1));

        Assert.False(timer.ShouldLeave(anyoneListening: true));
    }

    [Fact]
    public void EmptyChannel_DoesNotLeaveImmediately()
    {
        // The countdown starts on the first empty observation rather than firing at once,
        // so a momentary gap while someone reconnects does not eject the bot.
        var (timer, _) = Create();

        Assert.False(timer.ShouldLeave(anyoneListening: false));
    }

    [Fact]
    public void EmptyChannel_LeavesOnceTheTimeoutElapses()
    {
        var (timer, time) = Create();
        timer.ShouldLeave(anyoneListening: false);

        time.Advance(Timeout + TimeSpan.FromMilliseconds(1));

        Assert.True(timer.ShouldLeave(anyoneListening: false));
    }

    [Fact]
    public void EmptyChannel_StaysWhileStillInsideTheTimeout()
    {
        var (timer, time) = Create();
        timer.ShouldLeave(anyoneListening: false);

        time.Advance(Timeout - TimeSpan.FromMilliseconds(1));

        Assert.False(timer.ShouldLeave(anyoneListening: false));
    }

    [Fact]
    public void SomeoneRejoining_RestartsTheCountdown()
    {
        var (timer, time) = Create();
        timer.ShouldLeave(anyoneListening: false);
        time.Advance(TimeSpan.FromSeconds(4));

        // Someone comes back, then leaves again: the clock starts over rather than the
        // bot dropping out a second later.
        Assert.False(timer.ShouldLeave(anyoneListening: true));
        Assert.False(timer.ShouldLeave(anyoneListening: false));

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(timer.ShouldLeave(anyoneListening: false));

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(timer.ShouldLeave(anyoneListening: false));
    }

    [Fact]
    public void UnknownOccupancy_IsTreatedAsStillOccupied()
    {
        // The caller passes "anyoneListening: true" when the guild cache is momentarily
        // unavailable. That window is short, but with a 5 second timeout it is long enough
        // to eject the bot from a channel people are still sitting in.
        var (timer, time) = Create();

        timer.ShouldLeave(anyoneListening: false);
        time.Advance(TimeSpan.FromSeconds(4));

        // Cache goes away: treated as occupied, so the countdown restarts.
        Assert.False(timer.ShouldLeave(anyoneListening: true));

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(timer.ShouldLeave(anyoneListening: false));
    }

    [Fact]
    public void ZeroTimeout_DisablesAutomaticLeaving()
    {
        var timer = new IdleTimer(TimeSpan.Zero, new FakeTimeProvider());

        Assert.False(timer.ShouldLeave(anyoneListening: false));
    }
}
