using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;
using SemperSounds.Core.EntrySounds;

namespace SemperSounds.Tests;

public sealed class EntrySoundLibraryTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 2002;

    private readonly SqliteConnection _connection;
    private readonly SoundboardDbContext _db;
    private readonly EntrySoundLibrary _entrySounds;

    public EntrySoundLibraryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _db = new SoundboardDbContext(
            new DbContextOptionsBuilder<SoundboardDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _entrySounds = new EntrySoundLibrary(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Sound> AddSoundAsync(string name, int durationMs = 2000)
    {
        var sound = new Sound
        {
            Name = name,
            UploaderId = 42,
            UploaderName = "alkobot",
            DurationMs = durationMs,
        };
        _db.Sounds.Add(sound);
        await _db.SaveChangesAsync();
        return sound;
    }

    private async Task SetMaxDurationAsync(int milliseconds)
    {
        var settings = await _db.EntrySoundSettings.SingleAsync();
        settings.MaxDurationMs = milliseconds;
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Settings_AreSeededSoThereIsNeverAMissingRow()
    {
        // Seeded through the model rather than by startup code, so it exists after both a
        // migration and the EnsureCreated() used here. That is what lets every read site
        // skip a "what if there is no settings row" branch.
        var settings = await _entrySounds.GetSettingsAsync();

        Assert.True(settings.IsEnabled);
        Assert.Null(settings.SnoozedUntil);
        Assert.Equal(70, settings.VolumePercent);
        Assert.Equal(1, await _db.EntrySoundSettings.CountAsync());
    }

    [Fact]
    public async Task AssigningASound_StoresItAgainstTheUser()
    {
        var sound = await AddSoundAsync("airhorn");

        var result = await _entrySounds.AssignAsync(Alice, sound.Id);

        Assert.True(result.IsSuccess);
        var assignment = await _entrySounds.FindAsync(Alice);
        Assert.Equal(sound.Id, assignment?.SoundId);
        Assert.Equal("airhorn", assignment?.Sound?.Name);
        Assert.False(assignment?.IsMuted);
    }

    [Fact]
    public async Task AssigningASecondSound_ReplacesTheFirst()
    {
        // One entry sound per person is a unique index, not just a convention, so picking
        // again has to update the row rather than insert beside it.
        var first = await AddSoundAsync("airhorn");
        var second = await AddSoundAsync("yeet");

        await _entrySounds.AssignAsync(Alice, first.Id);
        await _entrySounds.AssignAsync(Alice, second.Id);

        Assert.Equal(second.Id, (await _entrySounds.FindAsync(Alice))?.SoundId);
        Assert.Equal(1, await _db.EntrySounds.CountAsync(e => e.UserId == Alice));
    }

    [Fact]
    public async Task UsersDoNotShareAnAssignment()
    {
        var airhorn = await AddSoundAsync("airhorn");
        var yeet = await AddSoundAsync("yeet");

        await _entrySounds.AssignAsync(Alice, airhorn.Id);
        await _entrySounds.AssignAsync(Bob, yeet.Id);

        Assert.Equal(airhorn.Id, (await _entrySounds.FindAsync(Alice))?.SoundId);
        Assert.Equal(yeet.Id, (await _entrySounds.FindAsync(Bob))?.SoundId);
    }

    [Fact]
    public async Task AssigningASoundThatDoesNotExist_IsRefused()
    {
        var result = await _entrySounds.AssignAsync(Alice, Guid.CreateVersion7());

        Assert.False(result.IsSuccess);
        Assert.Null(await _entrySounds.FindAsync(Alice));
    }

    [Fact]
    public async Task SoundLongerThanTheCap_IsRefused()
    {
        await SetMaxDurationAsync(3000);
        var sound = await AddSoundAsync("monologue", durationMs: 4200);

        var result = await _entrySounds.AssignAsync(Alice, sound.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains("3", result.Error);
        Assert.Null(await _entrySounds.FindAsync(Alice));
    }

    [Fact]
    public async Task LoweringTheCap_LeavesExistingAssignmentsAlone()
    {
        // The cap is applied when picking, never when playing. Tightening it must not
        // silently unassign people who chose a longer clip while it was allowed.
        var sound = await AddSoundAsync("monologue", durationMs: 4200);
        Assert.True((await _entrySounds.AssignAsync(Alice, sound.Id)).IsSuccess);

        await SetMaxDurationAsync(3000);

        Assert.Equal(sound.Id, (await _entrySounds.FindAsync(Alice))?.SoundId);
    }

    [Fact]
    public async Task DeletingASound_RemovesTheAssignment()
    {
        // Cascade, matching Favorite: an assignment pointing at a deleted sound is a
        // dangling pointer, not history worth keeping.
        var sound = await AddSoundAsync("airhorn");
        await _entrySounds.AssignAsync(Alice, sound.Id);

        _db.Sounds.Remove(sound);
        await _db.SaveChangesAsync();

        Assert.Null(await _entrySounds.FindAsync(Alice));
    }

    [Fact]
    public async Task MutingKeepsTheAssignment()
    {
        // Muting is not unassigning: turning it back on should cost one click.
        var sound = await AddSoundAsync("airhorn");
        await _entrySounds.AssignAsync(Alice, sound.Id);

        await _entrySounds.SetMutedAsync(Alice, true);

        var assignment = await _entrySounds.FindAsync(Alice);
        Assert.True(assignment?.IsMuted);
        Assert.Equal(sound.Id, assignment?.SoundId);

        await _entrySounds.SetMutedAsync(Alice, false);
        Assert.False((await _entrySounds.FindAsync(Alice))?.IsMuted);
    }

    [Fact]
    public async Task ReassigningKeepsTheMutePreference()
    {
        // Someone who has muted themselves and then changes their pick has not asked to
        // start making noise again.
        var first = await AddSoundAsync("airhorn");
        var second = await AddSoundAsync("yeet");
        await _entrySounds.AssignAsync(Alice, first.Id);
        await _entrySounds.SetMutedAsync(Alice, true);

        await _entrySounds.AssignAsync(Alice, second.Id);

        var assignment = await _entrySounds.FindAsync(Alice);
        Assert.Equal(second.Id, assignment?.SoundId);
        Assert.True(assignment?.IsMuted);
    }

    [Fact]
    public async Task ClearingRemovesTheAssignment()
    {
        var sound = await AddSoundAsync("airhorn");
        await _entrySounds.AssignAsync(Alice, sound.Id);

        await _entrySounds.ClearAsync(Alice);

        Assert.Null(await _entrySounds.FindAsync(Alice));
    }

    [Fact]
    public async Task GetAll_ReturnsEveryAssignmentWithItsSound()
    {
        var airhorn = await AddSoundAsync("airhorn");
        var yeet = await AddSoundAsync("yeet");
        await _entrySounds.AssignAsync(Alice, airhorn.Id);
        await _entrySounds.AssignAsync(Bob, yeet.Id);

        var all = await _entrySounds.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.All(all, entry => Assert.NotNull(entry.Sound));
    }

    [Fact]
    public async Task UnblockedUser_HasNoBlockRecord()
    {
        Assert.Null(await _entrySounds.FindBlockAsync(Alice));
    }

    [Fact]
    public async Task BlockedUsers_AreListedForTheOverview()
    {
        // The page marks blocked people in a list of everyone, so it needs the set in one
        // query rather than a FindBlockAsync per row.
        _db.EntrySoundBlocks.Add(new EntrySoundBlock { UserId = Bob, Reason = "airhorn at 3am" });
        await _db.SaveChangesAsync();

        var blocked = await _entrySounds.GetBlockedUserIdsAsync();

        Assert.Equal([Bob], blocked);
        Assert.Equal("airhorn at 3am", (await _entrySounds.FindBlockAsync(Bob))?.Reason);
    }
}
