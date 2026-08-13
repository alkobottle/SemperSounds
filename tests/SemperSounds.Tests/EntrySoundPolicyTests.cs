using SemperSounds.Core.EntrySounds;

namespace SemperSounds.Tests;

public class EntrySoundPolicyTests
{
    private const ulong Alice = 1;
    private const ulong General = 100;
    private const ulong Gaming = 200;

    private static readonly Guid Airhorn = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = Now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Settings under which an otherwise valid arrival plays.</summary>
    private static EntrySoundSettingsSnapshot Settings => new(
        IsEnabled: true,
        SnoozedUntil: null,
        VolumePercent: 70,
        PerUserCooldownSeconds: 60,
        MaxDurationMs: 5000);

    /// <summary>An arrival that should play, so each test can spoil exactly one thing.</summary>
    private static EntrySoundRequest Arrival => new(
        UserId: Alice,
        ChannelId: General,
        BotChannelId: General,
        OtherHumansInChannel: 1,
        AssignedSoundId: Airhorn,
        IsSelfMuted: false,
        IsBlocked: false,
        LastEntryPlayedAt: null);

    private static (EntrySoundPolicy Policy, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider();
        return (new EntrySoundPolicy(time), time);
    }

    [Fact]
    public void AnOrdinaryArrival_PlaysTheAssignedSound()
    {
        var (policy, _) = Create();

        var decision = policy.Decide(Settings, Arrival);

        Assert.True(decision.ShouldPlay);
        Assert.Equal(Airhorn, decision.SoundId);
        Assert.Equal(EntrySoundRefusal.None, decision.Refusal);
    }

    [Fact]
    public void BotInAnotherChannel_PlaysNothing()
    {
        // The passive-only rule: the bot never follows anyone around, so an arrival
        // somewhere it is not connected is simply not our business.
        var (policy, _) = Create();

        var decision = policy.Decide(Settings, Arrival with { BotChannelId = Gaming });

        Assert.False(decision.ShouldPlay);
        Assert.Equal(EntrySoundRefusal.BotNotInChannel, decision.Refusal);
    }

    [Fact]
    public void BotNotConnectedAtAll_PlaysNothing()
    {
        var (policy, _) = Create();

        var decision = policy.Decide(Settings, Arrival with { BotChannelId = null });

        Assert.Equal(EntrySoundRefusal.BotNotInChannel, decision.Refusal);
    }

    [Fact]
    public void DisabledFeature_PlaysNothing()
    {
        var (policy, _) = Create();

        var decision = policy.Decide(Settings with { IsEnabled = false }, Arrival);

        Assert.Equal(EntrySoundRefusal.Disabled, decision.Refusal);
    }

    [Fact]
    public void ActiveSnooze_PlaysNothing()
    {
        var (policy, _) = Create();

        var decision = policy.Decide(Settings with { SnoozedUntil = Now.AddHours(1) }, Arrival);

        Assert.Equal(EntrySoundRefusal.Snoozed, decision.Refusal);
    }

    [Fact]
    public void ExpiredSnooze_PlaysAgainWithoutAnyoneClearingIt()
    {
        // The snooze is stored as an expiry and only ever compared, so nobody has to
        // remember to switch entry sounds back on and no timer has to fire.
        var (policy, time) = Create();
        var settings = Settings with { SnoozedUntil = Now.AddHours(1) };

        time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));

        Assert.True(policy.Decide(settings, Arrival).ShouldPlay);
    }

    [Fact]
    public void ChannelWithOnlyTheJoinerAndTheBot_PlaysNothing()
    {
        // Walking into an empty room should not make the bot talk to itself.
        var (policy, _) = Create();

        var decision = policy.Decide(Settings, Arrival with { OtherHumansInChannel = 0 });

        Assert.Equal(EntrySoundRefusal.NobodyToHearIt, decision.Refusal);
    }

    [Fact]
    public void UnknownOccupancy_PlaysNothing()
    {
        // Null means the guild cache was briefly unavailable, not that the channel is
        // empty. Note this is the deliberate opposite of IdleTimer, which treats unknown
        // occupancy as "someone is listening": both refuse to act on a guess, and there
        // the action being avoided is leaving while here it is playing.
        var (policy, _) = Create();

        var decision = policy.Decide(Settings, Arrival with { OtherHumansInChannel = null });

        Assert.Equal(EntrySoundRefusal.NobodyToHearIt, decision.Refusal);
    }

    [Fact]
    public void UserWithoutAnAssignment_PlaysNothing()
    {
        // Nobody gets a default sound; entry sounds are opt-in by picking one.
        var (policy, _) = Create();

        var decision = policy.Decide(Settings, Arrival with { AssignedSoundId = null });

        Assert.Equal(EntrySoundRefusal.NoAssignment, decision.Refusal);
    }

    [Fact]
    public void SelfMutedUser_PlaysNothing()
    {
        var (policy, _) = Create();

        var decision = policy.Decide(Settings, Arrival with { IsSelfMuted = true });

        Assert.Equal(EntrySoundRefusal.SelfMuted, decision.Refusal);
    }

    [Fact]
    public void BlockedUser_PlaysNothing()
    {
        var (policy, _) = Create();

        var decision = policy.Decide(Settings, Arrival with { IsBlocked = true });

        Assert.Equal(EntrySoundRefusal.Blocked, decision.Refusal);
    }

    [Fact]
    public void WithinCooldown_PlaysNothing()
    {
        // Leaving and rejoining is the obvious way to make a bot unbearable.
        var (policy, _) = Create();

        var decision = policy.Decide(
            Settings, Arrival with { LastEntryPlayedAt = Now.AddSeconds(-30) });

        Assert.Equal(EntrySoundRefusal.Cooldown, decision.Refusal);
    }

    [Fact]
    public void AfterCooldown_PlaysAgain()
    {
        var (policy, _) = Create();

        var decision = policy.Decide(
            Settings, Arrival with { LastEntryPlayedAt = Now.AddSeconds(-61) });

        Assert.True(decision.ShouldPlay);
    }

    [Fact]
    public void ZeroCooldown_IsDisabled()
    {
        // Matches SoundboardOptions.PerUserCooldownSeconds, where 0 means "no cooldown"
        // rather than "block everything".
        var (policy, _) = Create();

        var decision = policy.Decide(
            Settings with { PerUserCooldownSeconds = 0 },
            Arrival with { LastEntryPlayedAt = Now });

        Assert.True(decision.ShouldPlay);
    }

    [Fact]
    public void BeingInTheWrongChannel_IsReportedBeforeAnythingNeedingTheDatabase()
    {
        // The coordinator short-circuits on this refusal before opening a scope, so it has
        // to win over reasons that are only knowable after a query.
        var (policy, _) = Create();

        var decision = policy.Decide(
            Settings with { IsEnabled = false },
            Arrival with { BotChannelId = null, AssignedSoundId = null, IsBlocked = true });

        Assert.Equal(EntrySoundRefusal.BotNotInChannel, decision.Refusal);
    }
}
