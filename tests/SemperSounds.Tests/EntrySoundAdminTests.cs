using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;
using SemperSounds.Core.EntrySounds;

namespace SemperSounds.Tests;

public sealed class EntrySoundAdminTests : IDisposable
{
    private const ulong Boss = 9001;
    private const ulong Alice = 1001;
    private const ulong Bob = 2002;

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = Now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Everyone is an ordinary member except those named as administrators.</summary>
    private sealed class StubPermissions(bool? answer, params ulong[] administrators) : IGuildPermissions
    {
        public bool? IsAdministrator(ulong userId) =>
            administrators.Contains(userId) ? true : answer;
    }

    private readonly SqliteConnection _connection;
    private readonly SoundboardDbContext _db;
    private readonly EntrySoundLibrary _entrySounds;
    private readonly FakeTimeProvider _time = new();

    public EntrySoundAdminTests()
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

    /// <summary>An admin service acting as Boss, who is an administrator.</summary>
    private EntrySoundAdmin Admin() =>
        new(_db, new StubPermissions(false, Boss), _time);

    private EntrySoundAdmin AdminWith(bool? everyoneElse) =>
        new(_db, new StubPermissions(everyoneElse, Boss), _time);

    private async Task<Sound> AddSoundAsync(string name)
    {
        var sound = new Sound
        {
            Name = name,
            UploaderId = 42,
            UploaderName = "alkobot",
            DurationMs = 2000,
        };
        _db.Sounds.Add(sound);
        await _db.SaveChangesAsync();
        return sound;
    }

    [Fact]
    public async Task NonAdministrator_CannotChangeSettings()
    {
        // The rule the whole design rests on: authorization is enforced here, not by
        // hiding the page. A disabled button is a hint; this is what actually refuses.
        var result = await Admin().SetEnabledAsync(Alice, false);

        Assert.False(result.IsSuccess);
        Assert.True((await _entrySounds.GetSettingsAsync()).IsEnabled);
    }

    [Fact]
    public async Task UnknownPermissions_AreRefused()
    {
        // Null means the member cache has not arrived yet, which is common for a few
        // seconds after a deploy. Failing closed is the only safe reading.
        var result = await AdminWith(null).SetEnabledAsync(Alice, false);

        Assert.False(result.IsSuccess);
        Assert.True((await _entrySounds.GetSettingsAsync()).IsEnabled);
    }

    [Fact]
    public async Task Administrator_CanSwitchEntrySoundsOff()
    {
        var result = await Admin().SetEnabledAsync(Boss, false);

        Assert.True(result.IsSuccess);
        Assert.False((await _entrySounds.GetSettingsAsync()).IsEnabled);
    }

    [Fact]
    public async Task Snoozing_StoresAnExpiryRatherThanAFlag()
    {
        // An expiry needs no timer and nobody has to remember to switch it back on.
        await Admin().SnoozeAsync(Boss, TimeSpan.FromHours(2));

        var settings = await _entrySounds.GetSettingsAsync();
        Assert.Equal(Now.AddHours(2), settings.SnoozedUntil);
        Assert.True(settings.IsEnabled);
    }

    [Fact]
    public async Task Resuming_ClearsTheSnooze()
    {
        await Admin().SnoozeAsync(Boss, TimeSpan.FromHours(2));

        await Admin().ResumeAsync(Boss);

        Assert.Null((await _entrySounds.GetSettingsAsync()).SnoozedUntil);
    }

    [Fact]
    public async Task SettingsWrites_RecordWhoChangedThem()
    {
        await Admin().SetVolumeAsync(Boss, 30);

        var row = await _db.EntrySoundSettings.AsNoTracking().SingleAsync();
        Assert.Equal(30, row.VolumePercent);
        Assert.Equal(Boss, row.UpdatedByUserId);
        Assert.Equal(Now, row.UpdatedAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task VolumeOutsideTheAllowedRange_IsRefused(int percent)
    {
        var result = await Admin().SetVolumeAsync(Boss, percent);

        Assert.False(result.IsSuccess);
        Assert.Equal(70, (await _entrySounds.GetSettingsAsync()).VolumePercent);
    }

    [Fact]
    public async Task NegativeCooldown_IsRefused()
    {
        Assert.False((await Admin().SetCooldownAsync(Boss, -5)).IsSuccess);
        Assert.Equal(60, (await _entrySounds.GetSettingsAsync()).PerUserCooldownSeconds);
    }

    [Fact]
    public async Task ZeroCooldown_IsAllowedBecauseItMeansDisabled()
    {
        Assert.True((await Admin().SetCooldownAsync(Boss, 0)).IsSuccess);
        Assert.Equal(0, (await _entrySounds.GetSettingsAsync()).PerUserCooldownSeconds);
    }

    [Fact]
    public async Task BlockingSomeoneWithNoAssignment_IsRecorded()
    {
        // Pre-blocking has to work, which is the reason the block is its own table rather
        // than a flag on an assignment row that may not exist.
        var result = await Admin().BlockAsync(Boss, "boss", Bob, "airhorn at 3am");

        Assert.True(result.IsSuccess);
        var block = await _entrySounds.FindBlockAsync(Bob);
        Assert.Equal("airhorn at 3am", block?.Reason);
        Assert.Equal(Boss, block?.BlockedByUserId);
        Assert.Equal("boss", block?.BlockedByName);
    }

    [Fact]
    public async Task BlockSurvivesReassignment()
    {
        // The other reason for a separate table: on the assignment row, clearing and
        // re-picking a sound would quietly launder the block away.
        var airhorn = await AddSoundAsync("airhorn");
        var yeet = await AddSoundAsync("yeet");
        await _entrySounds.AssignAsync(Bob, airhorn.Id);
        await Admin().BlockAsync(Boss, "boss", Bob, "too loud");

        await _entrySounds.ClearAsync(Bob);
        await _entrySounds.AssignAsync(Bob, yeet.Id);

        Assert.NotNull(await _entrySounds.FindBlockAsync(Bob));
    }

    [Fact]
    public async Task BlockingTwice_UpdatesTheReasonRatherThanFailing()
    {
        // UserId is unique, so a second block has to be an update or it throws.
        await Admin().BlockAsync(Boss, "boss", Bob, "first reason");

        var result = await Admin().BlockAsync(Boss, "boss", Bob, "second reason");

        Assert.True(result.IsSuccess);
        Assert.Equal("second reason", (await _entrySounds.FindBlockAsync(Bob))?.Reason);
        Assert.Equal(1, await _db.EntrySoundBlocks.CountAsync());
    }

    [Fact]
    public async Task Unblocking_RemovesTheBlock()
    {
        await Admin().BlockAsync(Boss, "boss", Bob, "too loud");

        var result = await Admin().UnblockAsync(Boss, Bob);

        Assert.True(result.IsSuccess);
        Assert.Null(await _entrySounds.FindBlockAsync(Bob));
    }

    [Fact]
    public async Task NonAdministrator_CannotBlockAnyone()
    {
        var result = await Admin().BlockAsync(Alice, "alice", Bob, "I do not like him");

        Assert.False(result.IsSuccess);
        Assert.Null(await _entrySounds.FindBlockAsync(Bob));
    }

    [Fact]
    public async Task NonAdministrator_CannotUnblockThemselves()
    {
        await Admin().BlockAsync(Boss, "boss", Bob, "too loud");

        var result = await Admin().UnblockAsync(Bob, Bob);

        Assert.False(result.IsSuccess);
        Assert.NotNull(await _entrySounds.FindBlockAsync(Bob));
    }

    [Fact]
    public async Task TighteningTheDurationCap_DoesNotTouchExistingAssignments()
    {
        var sound = await AddSoundAsync("airhorn");
        await _entrySounds.AssignAsync(Alice, sound.Id);

        Assert.True((await Admin().SetMaxDurationAsync(Boss, 1000)).IsSuccess);

        Assert.NotNull(await _entrySounds.FindAsync(Alice));
        Assert.Equal(1000, (await _entrySounds.GetSettingsAsync()).MaxDurationMs);
    }
}
