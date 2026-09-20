using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;
using SemperSounds.Core.Devices;

namespace SemperSounds.Tests;

/// <summary>
/// The credential that lets a desktop client act as somebody. Every one of these pins a way
/// the store could fail <em>open</em> — hand out a working token it should have refused, or
/// keep accepting one the user believes they have withdrawn.
/// </summary>
public sealed class DeviceTokenStoreTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 2002;

    private readonly SqliteConnection _connection;
    private readonly SoundboardDbContext _db;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly DeviceTokenStore _store;

    public DeviceTokenStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _db = new SoundboardDbContext(new DbContextOptionsBuilder<SoundboardDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _store = new DeviceTokenStore(_db, _time);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Task<string> IssueAsync(ulong user = Alice, string name = "Desktop") =>
        _store.IssueAsync(user, "alice", name);

    [Fact]
    public async Task AnIssuedToken_Validates()
    {
        var token = await IssueAsync();

        var identity = await _store.ValidateAsync(token);

        Assert.NotNull(identity);
        Assert.Equal(Alice, identity.Value.UserId);
        Assert.Equal("alice", identity.Value.UserName);
    }

    [Fact]
    public async Task ThePlaintext_IsNeverStored()
    {
        // The whole point of hashing. A row that can hand out a working credential is worse
        // than a user having to pair again.
        var token = await IssueAsync();

        var stored = await _db.DeviceTokens.AsNoTracking().SingleAsync();

        Assert.DoesNotContain(token, stored.TokenHash, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DeviceToken.HashLength, stored.TokenHash.Length);
    }

    [Fact]
    public async Task TwoIssues_DifferEvenForTheSameUserAndName()
    {
        Assert.NotEqual(await IssueAsync(), await IssueAsync());
    }

    [Fact]
    public async Task AnUnknownToken_DoesNotValidate()
    {
        await IssueAsync();

        Assert.Null(await _store.ValidateAsync("ss_not-a-real-token"));
    }

    [Fact]
    public async Task GarbageInput_DoesNotThrow()
    {
        // Reached by anyone curling the hub. An exception here would be a 500 per probe.
        Assert.Null(await _store.ValidateAsync(""));
        Assert.Null(await _store.ValidateAsync("   "));
        Assert.Null(await _store.ValidateAsync(new string('x', 5000)));
    }

    [Fact]
    public async Task ARevokedToken_StopsValidating()
    {
        var token = await IssueAsync();
        var id = (await _store.ListAsync(Alice)).Single().Id;

        Assert.True(await _store.RevokeAsync(id, Alice));

        Assert.Null(await _store.ValidateAsync(token));
    }

    [Fact]
    public async Task RevokingSomebodyElsesDevice_Fails()
    {
        // The acting user is checked in the store, not in the page, for the same reason
        // EntrySoundAdmin takes one: hiding a button is not what protects the operation.
        var token = await IssueAsync();
        var id = (await _store.ListAsync(Alice)).Single().Id;

        Assert.False(await _store.RevokeAsync(id, Bob));
        Assert.NotNull(await _store.ValidateAsync(token));
    }

    [Fact]
    public async Task ARevokedRow_SurvivesSoTheUserCanSeeWhatHappened()
    {
        await IssueAsync();
        var id = (await _store.ListAsync(Alice)).Single().Id;

        await _store.RevokeAsync(id, Alice);

        var row = await _db.DeviceTokens.AsNoTracking().SingleAsync();
        Assert.NotNull(row.RevokedAt);
    }

    [Fact]
    public async Task AnExpiredToken_StopsValidating()
    {
        // Guild membership is only checked at pairing time. Without the expiry a token would
        // outlive the user leaving the Discord server entirely.
        var token = await IssueAsync();

        _time.Advance(DeviceToken.Lifetime + TimeSpan.FromMinutes(1));

        Assert.Null(await _store.ValidateAsync(token));
    }

    [Fact]
    public async Task UsingAToken_RenewsIt()
    {
        var token = await IssueAsync();

        // Just short of expiry, then past the original one: an active device never notices.
        _time.Advance(DeviceToken.Lifetime - TimeSpan.FromDays(1));
        Assert.NotNull(await _store.ValidateAsync(token));

        _time.Advance(TimeSpan.FromDays(2));
        Assert.NotNull(await _store.ValidateAsync(token));
    }

    [Fact]
    public async Task LastUsed_IsNotWrittenOnEveryCall()
    {
        // A reconnecting client would otherwise write a row per connect, which is a lot of
        // churn for a column nobody reads more precisely than "recently".
        var token = await IssueAsync();
        _time.Advance(TimeSpan.FromDays(1));
        await _store.ValidateAsync(token);

        var afterFirst = (await _store.ListAsync(Alice)).Single().LastUsedAt;

        _time.Advance(TimeSpan.FromSeconds(30));
        await _store.ValidateAsync(token);

        Assert.Equal(afterFirst, (await _store.ListAsync(Alice)).Single().LastUsedAt);
    }

    [Fact]
    public async Task IssuingBeyondTheCap_Fails()
    {
        for (var i = 0; i < DeviceToken.MaxPerUser; i++)
        {
            await IssueAsync(name: $"PC {i}");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => IssueAsync(name: "One too many"));
    }

    [Fact]
    public async Task RevokedDevices_DoNotCountTowardsTheCap()
    {
        // Otherwise re-pairing the same PC ten times locks the user out permanently.
        for (var i = 0; i < DeviceToken.MaxPerUser; i++)
        {
            await IssueAsync(name: $"PC {i}");
        }

        await _store.RevokeAsync((await _store.ListAsync(Alice)).First().Id, Alice);

        Assert.NotNull(await IssueAsync(name: "Replacement"));
    }

    [Fact]
    public async Task OneUsersCap_DoesNotAffectAnother()
    {
        for (var i = 0; i < DeviceToken.MaxPerUser; i++)
        {
            await IssueAsync(name: $"PC {i}");
        }

        Assert.NotNull(await _store.IssueAsync(Bob, "bob", "Bob's PC"));
    }

    [Fact]
    public async Task ListAsync_ShowsOnlyYourOwnDevices()
    {
        await IssueAsync();
        await _store.IssueAsync(Bob, "bob", "Bob's PC");

        var mine = await _store.ListAsync(Alice);

        Assert.Equal("Desktop", Assert.Single(mine).DeviceName);
    }

    /// <summary>A clock the tests drive, since TimeProvider is not registered in DI.</summary>
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
