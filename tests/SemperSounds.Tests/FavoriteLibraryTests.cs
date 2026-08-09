using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;
using SemperSounds.Core.Sounds;

namespace SemperSounds.Tests;

public sealed class FavoriteLibraryTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 2002;

    private readonly SqliteConnection _connection;
    private readonly SoundboardDbContext _db;
    private readonly FavoriteLibrary _favorites;

    public FavoriteLibraryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _db = new SoundboardDbContext(
            new DbContextOptionsBuilder<SoundboardDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _favorites = new FavoriteLibrary(_db);
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

    private async Task<List<Guid>> StarManyAsync(ulong userId, int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var sound = await AddSoundAsync($"sound-{i}");
            await _favorites.ToggleAsync(userId, sound.Id);
            ids.Add(sound.Id);
        }
        return ids;
    }

    [Fact]
    public async Task FirstFavorite_TakesSlotOne()
    {
        var sound = await AddSoundAsync("airhorn");

        var result = await _favorites.ToggleAsync(Alice, sound.Id);

        Assert.True(result.IsFavorited);
        var favorites = await _favorites.GetForUserAsync(Alice);
        Assert.Equal(1, Assert.Single(favorites).Slot);
    }

    [Fact]
    public async Task Favorites_AreNumberedInOrderOfStarring()
    {
        await StarManyAsync(Alice, 3);

        var favorites = await _favorites.GetForUserAsync(Alice);

        Assert.Equal([1, 2, 3], favorites.Select(f => f.Slot));
    }

    [Fact]
    public async Task Unstarring_CompactsTheRemainingSlots()
    {
        // Gaps would leave keys like 1, 3, 4 with nothing on 2, so the slots close up.
        var ids = await StarManyAsync(Alice, 4);

        await _favorites.ToggleAsync(Alice, ids[1]);

        var favorites = await _favorites.GetForUserAsync(Alice);
        Assert.Equal([1, 2, 3], favorites.Select(f => f.Slot));
        Assert.Equal([ids[0], ids[2], ids[3]], favorites.Select(f => f.SoundId));
    }

    [Fact]
    public async Task StarringTwice_RemovesTheFavorite()
    {
        var sound = await AddSoundAsync("airhorn");

        await _favorites.ToggleAsync(Alice, sound.Id);
        var second = await _favorites.ToggleAsync(Alice, sound.Id);

        Assert.False(second.IsFavorited);
        Assert.Empty(await _favorites.GetForUserAsync(Alice));
    }

    [Fact]
    public async Task TenthFavorite_IsRefusedRatherThanEvictingAnExistingOne()
    {
        // Silently dropping a favourite would defeat the point: the keys are supposed to
        // stay exactly where the user left them.
        await StarManyAsync(Alice, FavoriteLibrary.MaxSlots);
        var extra = await AddSoundAsync("one too many");

        var result = await _favorites.ToggleAsync(Alice, extra.Id);

        Assert.False(result.IsFavorited);
        Assert.NotEmpty(result.Error);
        Assert.Equal(FavoriteLibrary.MaxSlots, (await _favorites.GetForUserAsync(Alice)).Count);
    }

    [Fact]
    public async Task Favorites_AreIsolatedPerUser()
    {
        var shared = await AddSoundAsync("airhorn");
        var mine = await AddSoundAsync("kekw");

        await _favorites.ToggleAsync(Alice, shared.Id);
        await _favorites.ToggleAsync(Alice, mine.Id);
        await _favorites.ToggleAsync(Bob, shared.Id);

        var aliceFavorites = await _favorites.GetForUserAsync(Alice);
        var bobFavorites = await _favorites.GetForUserAsync(Bob);

        Assert.Equal(2, aliceFavorites.Count);
        Assert.Equal(shared.Id, Assert.Single(bobFavorites).SoundId);
        // Both hold the same sound on slot 1 without colliding.
        Assert.Equal(1, bobFavorites[0].Slot);
        Assert.Equal(1, aliceFavorites[0].Slot);
    }

    [Fact]
    public async Task MovingUp_SwapsWithTheFavoriteAbove()
    {
        var ids = await StarManyAsync(Alice, 3);

        await _favorites.MoveAsync(Alice, ids[2], up: true);

        var favorites = await _favorites.GetForUserAsync(Alice);
        Assert.Equal([ids[0], ids[2], ids[1]], favorites.Select(f => f.SoundId));
        Assert.Equal([1, 2, 3], favorites.Select(f => f.Slot));
    }

    [Fact]
    public async Task MovingTheFirstFavoriteUp_DoesNothing()
    {
        var ids = await StarManyAsync(Alice, 2);

        await _favorites.MoveAsync(Alice, ids[0], up: true);

        Assert.Equal(ids, (await _favorites.GetForUserAsync(Alice)).Select(f => f.SoundId));
    }

    [Fact]
    public async Task DeletingASound_RemovesItFromEveryonesFavorites()
    {
        // Anyone in the server can delete a sound, so a favourite must not survive as a
        // dangling pointer -- unlike the play log, which deliberately does survive.
        var sound = await AddSoundAsync("doomed");
        await _favorites.ToggleAsync(Alice, sound.Id);
        await _favorites.ToggleAsync(Bob, sound.Id);

        _db.Sounds.Remove(await _db.Sounds.FirstAsync(s => s.Id == sound.Id));
        await _db.SaveChangesAsync();

        Assert.Empty(await _favorites.GetForUserAsync(Alice));
        Assert.Empty(await _favorites.GetForUserAsync(Bob));
    }

    [Fact]
    public async Task GetForUser_IncludesTheSoundSoTilesCanRender()
    {
        var sound = await AddSoundAsync("airhorn");
        await _favorites.ToggleAsync(Alice, sound.Id);

        var favorite = Assert.Single(await _favorites.GetForUserAsync(Alice));

        Assert.Equal("airhorn", favorite.Sound!.Name);
    }
}
