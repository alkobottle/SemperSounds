using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;
using SemperSounds.Core.Sounds;

namespace SemperSounds.Tests;

public sealed class ActivityLogTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SoundboardDbContext _db;
    private readonly ActivityLog _log;

    public ActivityLogTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new SoundboardDbContext(
            new DbContextOptionsBuilder<SoundboardDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _log = new ActivityLog(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task EntrySounds_AreLoggedAsTheirOwnKind()
    {
        // Not SoundboardActivity.Played: that reads as "this person pressed a button" and
        // would credit them with an action they did not take, in the one place people go to
        // work out who did what. ActivityLine renders the two differently.
        var sound = new Sound { Name = "airhorn", UploaderId = 1, UploaderName = "alkobot" };
        _db.Sounds.Add(sound);
        await _db.SaveChangesAsync();

        await _log.LogEntrySoundAsync(sound, 7, "mace", 99, "VAL");

        var entry = Assert.Single(await _log.GetRecentAsync(10));
        Assert.Equal(SoundboardActivity.EntryPlayed, entry.Kind);
        Assert.Equal("airhorn", entry.SoundName);
        Assert.Equal(7ul, entry.UserId);

        // IsAutomatic means "the bot left because nobody remained" and must keep meaning
        // exactly that — an entry sound is not a departure.
        Assert.False(entry.IsAutomatic);
    }

    [Fact]
    public async Task PlaysJoinsAndLeaves_ShareOneChronologicalStream()
    {
        var sound = new Sound { Name = "airhorn", UploaderId = 1, UploaderName = "alkobot" };
        _db.Sounds.Add(sound);
        await _db.SaveChangesAsync();

        await _log.LogJoinAsync(1, "alkobot", 99, "VAL");
        await Task.Delay(5);
        await _log.LogPlayAsync(sound, 1, "alkobot", 99, "VAL");
        await Task.Delay(5);
        await _log.LogLeaveAsync(1, "alkobot", 99, "VAL");

        var recent = await _log.GetRecentAsync(10);

        Assert.Equal(
            [SoundboardActivity.Left, SoundboardActivity.Played, SoundboardActivity.Joined],
            recent.Select(e => e.Kind));
    }

    [Fact]
    public async Task AutomaticLeave_HasNoUserAndIsMarkedAutomatic()
    {
        // This is how the UI tells "someone sent the bot away" from "nobody was left".
        await _log.LogLeaveAsync(userId: null, userName: null, 99, "VAL");

        var entry = Assert.Single(await _log.GetRecentAsync(10));

        Assert.Null(entry.UserId);
        Assert.True(entry.IsAutomatic);
    }

    [Fact]
    public async Task UserInitiatedLeave_IsNotMarkedAutomatic()
    {
        await _log.LogLeaveAsync(7, "mace", 99, "VAL");

        var entry = Assert.Single(await _log.GetRecentAsync(10));

        Assert.False(entry.IsAutomatic);
        Assert.Equal(7ul, entry.UserId);
    }

    [Fact]
    public async Task JoinAndLeave_CarryNoSound()
    {
        await _log.LogJoinAsync(1, "alkobot", 99, "VAL");

        var entry = Assert.Single(await _log.GetRecentAsync(10));

        Assert.Null(entry.SoundId);
        Assert.Null(entry.SoundName);
        Assert.Equal("VAL", entry.ChannelName);
    }
}
