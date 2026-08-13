using SemperSounds.Web.Services;

namespace SemperSounds.Tests;

public class VoiceTransitionJournalTests
{
    private const ulong Alice = 1;
    private const ulong Bob = 2;
    private const ulong General = 100;
    private const ulong Gaming = 200;

    private static VoiceTransitionJournal Seeded(params (ulong UserId, ulong ChannelId)[] occupants)
    {
        var journal = new VoiceTransitionJournal();
        journal.Seed(occupants);
        return journal;
    }

    [Fact]
    public void ArrivingInAChannel_IsAJoin()
    {
        var journal = Seeded();

        var movement = journal.Observe(Alice, General);

        Assert.Equal(VoiceTransition.Joined, movement.Kind);
        Assert.Equal(General, movement.ToChannelId);
        Assert.Null(movement.FromChannelId);
        Assert.True(movement.IsArrival);
    }

    [Fact]
    public void MuteUpdateInTheSameChannel_IsNotAJoin()
    {
        // The whole reason this type exists. Discord sends VOICE_STATE_UPDATE for muting,
        // deafening and turning the camera on, all carrying the unchanged channel. Treating
        // the raw event as an arrival would replay someone's entry sound every time they
        // touched their microphone.
        var journal = Seeded((Alice, General));

        var movement = journal.Observe(Alice, General);

        Assert.Equal(VoiceTransition.None, movement.Kind);
        Assert.False(movement.IsArrival);
    }

    [Fact]
    public void FirstUpdateForSomeoneAlreadyInVoice_IsNotAJoin()
    {
        // Seeding from GUILD_CREATE is what makes this true: without it the first event
        // after every reconnect would look like an arrival for everyone already sitting
        // in voice, firing a burst of entry sounds on restart.
        var journal = Seeded((Alice, General), (Bob, Gaming));

        Assert.Equal(VoiceTransition.None, journal.Observe(Alice, General).Kind);
        Assert.Equal(VoiceTransition.None, journal.Observe(Bob, Gaming).Kind);
    }

    [Fact]
    public void ObservationBeforeSeeding_IsNeverAnArrival()
    {
        // Between process start and GUILD_CREATE there is no previous state to compare
        // against, so nothing can be said honestly about what changed.
        var journal = new VoiceTransitionJournal();

        var movement = journal.Observe(Alice, General);

        Assert.False(journal.IsSeeded);
        Assert.Equal(VoiceTransition.None, movement.Kind);
    }

    [Fact]
    public void MovingBetweenChannels_ReportsTheDestination()
    {
        var journal = Seeded((Alice, General));

        var movement = journal.Observe(Alice, Gaming);

        Assert.Equal(VoiceTransition.Moved, movement.Kind);
        Assert.Equal(General, movement.FromChannelId);
        Assert.Equal(Gaming, movement.ToChannelId);
        Assert.True(movement.IsArrival);
    }

    [Fact]
    public void LeavingVoice_ReportsLeft_AndTheNextArrivalIsAJoinAgain()
    {
        var journal = Seeded((Alice, General));

        var left = journal.Observe(Alice, null);

        Assert.Equal(VoiceTransition.Left, left.Kind);
        Assert.Equal(General, left.FromChannelId);
        Assert.Null(left.ToChannelId);
        Assert.False(left.IsArrival);

        // The departure has to actually forget her, or coming back reads as "unchanged".
        Assert.Equal(VoiceTransition.Joined, journal.Observe(Alice, General).Kind);
    }

    [Fact]
    public void LeavingWhenWeNeverSawThemArrive_IsNotATransition()
    {
        var journal = Seeded();

        Assert.Equal(VoiceTransition.None, journal.Observe(Alice, null).Kind);
    }

    [Fact]
    public void Seeding_ReplacesTheWholeJournal()
    {
        // A session invalidation is repaired by a fresh IDENTIFY, which re-delivers
        // GUILD_CREATE. Merging instead of replacing would leave stale channels behind
        // for anyone who moved while the socket was down.
        var journal = Seeded((Alice, General), (Bob, Gaming));

        journal.Seed([(Bob, Gaming)]);

        Assert.Equal(VoiceTransition.Joined, journal.Observe(Alice, General).Kind);
        Assert.Equal(VoiceTransition.None, journal.Observe(Bob, Gaming).Kind);
    }

    [Fact]
    public void UsersAreTrackedIndependently()
    {
        var journal = Seeded((Alice, General));

        Assert.Equal(VoiceTransition.Joined, journal.Observe(Bob, General).Kind);
        Assert.Equal(VoiceTransition.None, journal.Observe(Alice, General).Kind);
    }
}
