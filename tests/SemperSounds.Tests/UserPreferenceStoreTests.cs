using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;
using SemperSounds.Core.Preferences;

namespace SemperSounds.Tests;

public sealed class UserPreferenceStoreTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 2002;

    private readonly SqliteConnection _connection;
    private readonly SoundboardDbContext _db;
    private readonly UserPreferenceStore _store;

    public UserPreferenceStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _db = new SoundboardDbContext(new DbContextOptionsBuilder<SoundboardDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _store = new UserPreferenceStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task NothingSaved_ReadsBackAsNull()
    {
        // Null has to be distinguishable from a saved empty string: it is what tells the
        // board this user is new, and therefore what triggers adopting whatever their browser
        // still holds from before preferences moved server side.
        Assert.Null(await _store.GetAsync(Alice, UserPreference.Keys.Board));
    }

    [Fact]
    public async Task AValue_SurvivesARoundTrip()
    {
        await _store.SaveAsync(Alice, UserPreference.Keys.Board, """{"sort":"MostPlayed"}""");

        Assert.Equal("""{"sort":"MostPlayed"}""", await _store.GetAsync(Alice, UserPreference.Keys.Board));
    }

    [Fact]
    public async Task SavingAgain_ReplacesRatherThanAccumulates()
    {
        await _store.SaveAsync(Alice, UserPreference.Keys.Board, "first");
        await _store.SaveAsync(Alice, UserPreference.Keys.Board, "second");

        Assert.Equal("second", await _store.GetAsync(Alice, UserPreference.Keys.Board));
        Assert.Equal(1, await _db.UserPreferences.CountAsync(p => p.UserId == Alice));
    }

    [Fact]
    public async Task OneUsersBoard_IsNotAnother()
    {
        // The whole point of moving this off the browser is that it follows the person. It
        // must not follow them onto somebody else's screen.
        await _store.SaveAsync(Alice, UserPreference.Keys.Board, "alice");
        await _store.SaveAsync(Bob, UserPreference.Keys.Board, "bob");

        Assert.Equal("alice", await _store.GetAsync(Alice, UserPreference.Keys.Board));
        Assert.Equal("bob", await _store.GetAsync(Bob, UserPreference.Keys.Board));
    }

    [Fact]
    public async Task KeysAreIndependent()
    {
        // The table is keyed on user *and* key so a future setting unrelated to the board can
        // share it without widening the row.
        await _store.SaveAsync(Alice, UserPreference.Keys.Board, "board value");
        await _store.SaveAsync(Alice, "something-else", "other value");

        Assert.Equal("board value", await _store.GetAsync(Alice, UserPreference.Keys.Board));
        Assert.Equal("other value", await _store.GetAsync(Alice, "something-else"));
    }

    [Fact]
    public async Task AnOversizedValue_IsRefusedRatherThanTruncated()
    {
        // Truncating would not fail to load, it would fail to *parse*, and the board answers
        // an unparseable blob with defaults — so a clipped write would read back as "you never
        // had any preferences" rather than as anything anyone could act on. Refusing leaves
        // the previous good value in place.
        await _store.SaveAsync(Alice, UserPreference.Keys.Board, "good");

        var saved = await _store.SaveAsync(
            Alice, UserPreference.Keys.Board, new string('x', UserPreference.MaxValueLength + 1));

        Assert.False(saved);
        Assert.Equal("good", await _store.GetAsync(Alice, UserPreference.Keys.Board));
    }

    [Fact]
    public async Task AValueAtTheLimit_IsAccepted()
    {
        var atLimit = new string('x', UserPreference.MaxValueLength);

        Assert.True(await _store.SaveAsync(Alice, UserPreference.Keys.Board, atLimit));
        Assert.Equal(atLimit, await _store.GetAsync(Alice, UserPreference.Keys.Board));
    }

    [Fact]
    public async Task AMissingKey_Throws()
    {
        // Unreachable from anything a user does, so it can only be an unfinished caller —
        // the one failure here worth being loud about.
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(Alice, "", "value"));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(Alice, "   ", "value"));
    }

    [Fact]
    public async Task TheSameUserAndKey_CannotBeStoredTwice()
    {
        // Enforced in the schema rather than only by the store's read-then-write, which two
        // circuits saving at the same moment can interleave. Without it the user ends up with
        // two rows and a coin flip over which one loads.
        await _store.SaveAsync(Alice, UserPreference.Keys.Board, "first");

        _db.UserPreferences.Add(new UserPreference
        {
            UserId = Alice,
            Key = UserPreference.Keys.Board,
            Value = "racing duplicate",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }
}
