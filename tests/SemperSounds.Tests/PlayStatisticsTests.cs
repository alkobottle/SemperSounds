using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;
using SemperSounds.Core.Statistics;

namespace SemperSounds.Tests;

public sealed class PlayStatisticsTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 2002;
    private const ulong Channel = 99;

    private static readonly DateTimeOffset Now = new(2026, 8, 13, 20, 0, 0, TimeSpan.Zero);

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = Now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private readonly SqliteConnection _connection;
    private readonly SoundboardDbContext _db;
    private readonly FakeTimeProvider _time = new();
    private readonly PlayStatistics _stats;

    public PlayStatisticsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _db = new SoundboardDbContext(
            new DbContextOptionsBuilder<SoundboardDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _stats = new PlayStatistics(_db, _time);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Sound> AddSoundAsync(string name)
    {
        var sound = new Sound { Name = name, UploaderId = 42, UploaderName = "alkobot" };
        _db.Sounds.Add(sound);
        await _db.SaveChangesAsync();
        return sound;
    }

    private async Task LogAsync(
        Guid soundId, DateTimeOffset when,
        SoundboardActivity kind = SoundboardActivity.Played, ulong? userId = Alice)
    {
        _db.ActivityLog.Add(new ActivityLogEntry
        {
            Kind = kind,
            SoundId = soundId,
            SoundName = "whatever",
            UserId = userId,
            UserName = "someone",
            ChannelId = Channel,
            OccurredAt = when,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task PerSoundStats_AggregateInSql_AgainstRealSqlite()
    {
        // MAX over a UtcTicksConverter column is the whole question. If the converter is
        // applied on the way in but not on the way back out, LastPlayedAt returns as year
        // 0001 rather than throwing — a green build and an in-memory test would both miss
        // it, and it would surface only when a tile rendered "last played 01/01/0001".
        var sound = await AddSoundAsync("airhorn");
        var earlier = Now.AddHours(-3);
        var later = Now.AddMinutes(-5);

        await LogAsync(sound.Id, earlier);
        await LogAsync(sound.Id, later);

        var stats = await _stats.GetPerSoundAsync();

        Assert.Equal(2, stats[sound.Id].Plays);
        Assert.Equal(later, stats[sound.Id].LastPlayedAt);
    }

    [Fact]
    public void PerSoundStats_RunsInSqlRatherThanInMemory()
    {
        // A correct result proves nothing about *where* it was computed: someone can make a
        // translation failure disappear with .AsEnumerable() and every other test here still
        // passes, while the app quietly drags the whole activity log into memory on every
        // board load. This is the only test that makes that impossible to do silently.
        var sql = _stats.BuildPerSoundQuery(Now).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAX(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EntrySoundPlays_AreNotCounted()
    {
        // The settled rule: the count answers "how often do people reach for this", so one
        // person rejoining voice forty times must not make their clip look popular.
        var sound = await AddSoundAsync("airhorn");

        await LogAsync(sound.Id, Now.AddMinutes(-10));
        await LogAsync(sound.Id, Now.AddMinutes(-5), SoundboardActivity.EntryPlayed);

        var stats = await _stats.GetPerSoundAsync();

        Assert.Equal(1, stats[sound.Id].Plays);
    }

    [Fact]
    public async Task SoundNobodyEverPressed_IsAbsentRatherThanZero()
    {
        // Callers treat a missing key as zero. Making that explicit here is what lets the
        // tile skip rendering "0" and the never-played filter be a simple absence check.
        var sound = await AddSoundAsync("airhorn");

        var stats = await _stats.GetPerSoundAsync();

        Assert.False(stats.ContainsKey(sound.Id));
    }

    [Fact]
    public async Task PlaysOfADeletedSound_StillCount()
    {
        // ActivityLogEntry.SoundId is deliberately not a foreign key so history survives a
        // deletion. The aggregate must not quietly assume the sound still exists.
        var sound = await AddSoundAsync("airhorn");
        await LogAsync(sound.Id, Now.AddHours(-1));

        _db.Sounds.Remove(sound);
        await _db.SaveChangesAsync();

        var stats = await _stats.GetPerSoundAsync();

        Assert.Equal(1, stats[sound.Id].Plays);
    }

    [Fact]
    public async Task JoinsAndLeaves_AreNotCounted()
    {
        // Those rows carry no SoundId, and automatic leaves carry no UserId either.
        var sound = await AddSoundAsync("airhorn");
        await LogAsync(sound.Id, Now.AddMinutes(-1));

        _db.ActivityLog.Add(new ActivityLogEntry
        {
            Kind = SoundboardActivity.Left,
            ChannelId = Channel,
            OccurredAt = Now,
        });
        await _db.SaveChangesAsync();

        var stats = await _stats.GetPerSoundAsync();

        Assert.Single(stats);
        Assert.Equal(1, stats[sound.Id].Plays);
    }

    [Fact]
    public async Task WeekWindows_SplitPlaysAgainstRealSqlite()
    {
        // Every window count compares a value-converted column against a parameter. Trending
        // and the stats page both rest on this translating correctly.
        var sound = await AddSoundAsync("airhorn");

        await LogAsync(sound.Id, Now.AddDays(-1));    // this week
        await LogAsync(sound.Id, Now.AddDays(-3));    // this week
        await LogAsync(sound.Id, Now.AddDays(-9));    // previous week
        await LogAsync(sound.Id, Now.AddDays(-40));   // older than both

        var stats = await _stats.GetPerSoundAsync();

        Assert.Equal(4, stats[sound.Id].Plays);
        Assert.Equal(2, stats[sound.Id].PlaysThisWeek);
        Assert.Equal(1, stats[sound.Id].PlaysPreviousWeek);
    }

    [Fact]
    public async Task WindowsMoveWithTheClock()
    {
        // Pins that the cutoffs are computed from TimeProvider on each call rather than
        // captured once, which would silently freeze trending at process start.
        var sound = await AddSoundAsync("airhorn");
        await LogAsync(sound.Id, Now.AddDays(-1));

        Assert.Equal(1, (await _stats.GetPerSoundAsync())[sound.Id].PlaysThisWeek);

        _time.Advance(TimeSpan.FromDays(10));

        Assert.Equal(0, (await _stats.GetPerSoundAsync())[sound.Id].PlaysThisWeek);
    }

    [Fact]
    public async Task SoundsAreCountedIndependently()
    {
        var airhorn = await AddSoundAsync("airhorn");
        var yeet = await AddSoundAsync("yeet");

        await LogAsync(airhorn.Id, Now.AddMinutes(-3));
        await LogAsync(airhorn.Id, Now.AddMinutes(-2), userId: Bob);
        await LogAsync(yeet.Id, Now.AddMinutes(-1));

        var stats = await _stats.GetPerSoundAsync();

        Assert.Equal(2, stats[airhorn.Id].Plays);
        Assert.Equal(1, stats[yeet.Id].Plays);
    }

    [Fact]
    public async Task SoundDetail_ReportsTheFirstAndLastPress()
    {
        var sound = await AddSoundAsync("airhorn");
        var first = Now.AddDays(-20);
        var last = Now.AddHours(-2);

        await LogAsync(sound.Id, first);
        await LogAsync(sound.Id, Now.AddDays(-9));
        await LogAsync(sound.Id, last);

        var detail = await _stats.GetForSoundAsync(sound.Id);

        Assert.Equal(3, detail.Plays);
        Assert.Equal(first, detail.FirstPlayedAt);
        Assert.Equal(last, detail.LastPlayedAt);
    }

    [Fact]
    public async Task SoundDetail_NamesWhoPressesItMost()
    {
        var sound = await AddSoundAsync("airhorn");

        await LogAsync(sound.Id, Now.AddMinutes(-5), userId: Bob);
        await LogAsync(sound.Id, Now.AddMinutes(-4), userId: Alice);
        await LogAsync(sound.Id, Now.AddMinutes(-3), userId: Alice);

        var detail = await _stats.GetForSoundAsync(sound.Id);

        Assert.Equal(Alice, detail.TopPlayer?.UserId);
        Assert.Equal(2, detail.TopPlayer?.Plays);
    }

    [Fact]
    public async Task SoundDetail_ForSomethingNobodyPressed_IsEmptyRatherThanNull()
    {
        // The dialog needs to render "never played" rather than blank or an exception.
        var sound = await AddSoundAsync("airhorn");

        var detail = await _stats.GetForSoundAsync(sound.Id);

        Assert.Equal(0, detail.Plays);
        Assert.Null(detail.FirstPlayedAt);
        Assert.Null(detail.LastPlayedAt);
        Assert.Null(detail.TopPlayer);
    }

    [Fact]
    public async Task SoundDetail_IgnoresEntrySoundsLikeEverythingElseHere()
    {
        var sound = await AddSoundAsync("airhorn");

        await LogAsync(sound.Id, Now.AddMinutes(-5));
        await LogAsync(sound.Id, Now.AddMinutes(-1), SoundboardActivity.EntryPlayed, Bob);

        var detail = await _stats.GetForSoundAsync(sound.Id);

        Assert.Equal(1, detail.Plays);
        Assert.Equal(Alice, detail.TopPlayer?.UserId);
    }

    [Fact]
    public async Task TopSounds_RankByPlaysAndKeepDeletedOnes()
    {
        // The log has no foreign key precisely so history survives deletion. Dropping a
        // deleted sound from the ranking would also make the totals stop adding up.
        var kept = await AddSoundAsync("kept");
        var doomed = await AddSoundAsync("doomed");

        await LogAsync(kept.Id, Now.AddMinutes(-3));
        await LogAsync(doomed.Id, Now.AddMinutes(-2));
        await LogAsync(doomed.Id, Now.AddMinutes(-1));

        _db.Sounds.Remove(doomed);
        await _db.SaveChangesAsync();

        var top = await _stats.GetTopSoundsAsync(10);

        Assert.Equal(2, top.Count);
        Assert.Equal(doomed.Id, top[0].SoundId);
        Assert.True(top[0].IsDeleted);
        Assert.False(top[1].IsDeleted);
        Assert.Equal("kept", top[1].SoundName);
    }

    [Fact]
    public async Task TopSounds_GroupByIdSoARenameDoesNotSplitARow()
    {
        // Anyone can rename a sound, and the log denormalizes the name at the time of
        // playing. Grouping by name would turn one sound into two half-height rows.
        var sound = await AddSoundAsync("original");
        await LogAsync(sound.Id, Now.AddMinutes(-5));

        sound.Name = "renamed";
        await _db.SaveChangesAsync();
        await LogAsync(sound.Id, Now.AddMinutes(-1));

        var top = await _stats.GetTopSoundsAsync(10);

        Assert.Single(top);
        Assert.Equal(2, top[0].Plays);
        Assert.Equal("renamed", top[0].SoundName);
    }

    [Fact]
    public async Task TopUsers_RankPressesAndIgnoreAutomaticLeaves()
    {
        // Automatic departures write a row with no user at all; without the guard they
        // would appear as a "nobody" bucket at the top of the list.
        var sound = await AddSoundAsync("airhorn");

        await LogAsync(sound.Id, Now.AddMinutes(-5), userId: Alice);
        await LogAsync(sound.Id, Now.AddMinutes(-4), userId: Alice);
        await LogAsync(sound.Id, Now.AddMinutes(-3), userId: Bob);
        await LogAsync(sound.Id, Now.AddMinutes(-2), SoundboardActivity.EntryPlayed, Bob);

        _db.ActivityLog.Add(new ActivityLogEntry
        {
            Kind = SoundboardActivity.Left,
            ChannelId = Channel,
            OccurredAt = Now,
        });
        await _db.SaveChangesAsync();

        var top = await _stats.GetTopUsersAsync(10);

        Assert.Equal(2, top.Count);
        Assert.Equal(Alice, top[0].UserId);
        Assert.Equal(2, top[0].Plays);
        Assert.Equal(1, top[1].Plays);
    }

    [Fact]
    public async Task PlaysPerDay_FillsQuietDaysWithZero()
    {
        // A chart with holes in it reads as missing data rather than a quiet Tuesday.
        var sound = await AddSoundAsync("airhorn");
        await LogAsync(sound.Id, Now);
        await LogAsync(sound.Id, Now.AddDays(-2));

        var days = await _stats.GetPlaysPerDayAsync(4);

        Assert.Equal(4, days.Count);
        Assert.Equal(0, days[0].Plays);
        Assert.Equal(1, days[1].Plays);
        Assert.Equal(0, days[2].Plays);
        Assert.Equal(1, days[3].Plays);
    }

    [Fact]
    public async Task PlaysPerDay_ReadsOnlyTheRequestedWindow()
    {
        // This one aggregates in memory, so the window clause is what keeps it O(recent
        // plays) rather than O(the whole log).
        var sound = await AddSoundAsync("airhorn");
        await LogAsync(sound.Id, Now.AddDays(-90));
        await LogAsync(sound.Id, Now);

        var days = await _stats.GetPlaysPerDayAsync(7);

        Assert.Equal(7, days.Count);
        Assert.Equal(1, days.Sum(day => day.Plays));
    }

    [Fact]
    public async Task TotalPlays_CountsPressesOnly()
    {
        var sound = await AddSoundAsync("airhorn");
        await LogAsync(sound.Id, Now.AddMinutes(-2));
        await LogAsync(sound.Id, Now.AddMinutes(-1), SoundboardActivity.EntryPlayed);

        Assert.Equal(1, await _stats.GetTotalPlaysAsync());
    }

    [Theory]
    // Rising and loud enough: trending.
    [InlineData(9, 4, true)]
    // A perennial favourite at a flat rate is popular, not trending — that is what the
    // most-played sort is for, and a badge that never turns off means nothing.
    [InlineData(40, 40, false)]
    // Falling off.
    [InlineData(6, 20, false)]
    // Rising, but too quiet to be worth a badge.
    [InlineData(4, 0, false)]
    public void Trending_MeasuresARiseRatherThanPopularity(int thisWeek, int previousWeek, bool expected)
    {
        var stats = new SoundPlayStats(Guid.CreateVersion7(), 100, thisWeek, previousWeek, Now);

        Assert.Equal(expected, stats.IsTrending);
    }

    [Fact]
    public void NeverPlayed_IsNotTrending()
    {
        var stats = new SoundPlayStats(Guid.CreateVersion7(), 0, 0, 0, null);

        Assert.False(stats.IsTrending);
    }
}
