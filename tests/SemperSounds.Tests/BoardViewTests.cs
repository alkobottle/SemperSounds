using SemperSounds.Core.Data;
using SemperSounds.Core.Statistics;
using SemperSounds.Web;

namespace SemperSounds.Tests;

public class BoardViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 20, 0, 0, TimeSpan.Zero);

    private static Sound MakeSound(
        string name, string tags = "", int durationMs = 2000,
        DateTimeOffset? uploadedAt = null, ulong uploaderId = 1, string emoji = "🙂") =>
        new()
        {
            Name = name,
            Tags = tags,
            Emoji = emoji,
            DurationMs = durationMs,
            UploaderId = uploaderId,
            UploaderName = $"user{uploaderId}",
            UploadedAt = uploadedAt ?? Now,
        };

    private static Dictionary<Guid, SoundPlayStats> Stats(params (Sound Sound, int Plays)[] entries) =>
        entries.ToDictionary(
            e => e.Sound.Id,
            e => new SoundPlayStats(e.Sound.Id, e.Plays, 0, 0, Now));

    private static List<string> Names(IEnumerable<Sound> sounds) => [.. sounds.Select(s => s.Name)];

    private static List<Sound> Apply(
        IEnumerable<Sound> sounds,
        BoardPreferences? preferences = null,
        string search = "",
        IReadOnlySet<Guid>? favorites = null,
        IReadOnlyDictionary<Guid, SoundPlayStats>? stats = null) =>
        BoardView.Apply(
            sounds,
            preferences ?? new BoardPreferences(),
            search,
            favorites ?? new HashSet<Guid>(),
            stats ?? new Dictionary<Guid, SoundPlayStats>());

    [Fact]
    public void DefaultPreferences_SortAlphabeticallyIgnoringCase()
    {
        // Deliberately not identical to the old SQL ordering. GetAllAsync ordered by name in
        // SQLite, whose default BINARY collation puts every capital before every lowercase
        // letter, so "Zebra" sorted above "apple". Sorting in memory fixes that.
        var sounds = new[] { MakeSound("Zebra"), MakeSound("apple"), MakeSound("Mango") };

        var result = Apply(sounds);

        Assert.Equal(["apple", "Mango", "Zebra"], Names(result));
    }

    [Fact]
    public void MostPlayed_OrdersByCountDescending()
    {
        var quiet = MakeSound("quiet");
        var loud = MakeSound("loud");
        var middling = MakeSound("middling");

        var result = Apply(
            [quiet, loud, middling],
            new BoardPreferences(Sort: BoardSort.MostPlayed),
            stats: Stats((quiet, 1), (loud, 99), (middling, 12)));

        Assert.Equal(["loud", "middling", "quiet"], Names(result));
    }

    [Fact]
    public void MostPlayed_TiesBreakByName()
    {
        // Most of the library sits at zero plays, so the bottom of this sort is one enormous
        // tie. Without a tiebreak the order there is arbitrary and shifts between renders.
        var sounds = new[] { MakeSound("zeta"), MakeSound("alpha"), MakeSound("mid") };

        var result = Apply(sounds, new BoardPreferences(Sort: BoardSort.MostPlayed));

        Assert.Equal(["alpha", "mid", "zeta"], Names(result));
    }

    [Fact]
    public void MostPlayed_PutsNeverPlayedLast()
    {
        var played = MakeSound("aaa-played");
        var never = MakeSound("bbb-never");

        var result = Apply(
            [never, played],
            new BoardPreferences(Sort: BoardSort.MostPlayed),
            stats: Stats((played, 3)));

        Assert.Equal(["aaa-played", "bbb-never"], Names(result));
    }

    [Fact]
    public void RecentlyUploaded_TiesBreakByName()
    {
        // The bulk importer stamps every clip it creates with the same UploadedAt, so ties
        // here are the normal case rather than an edge case.
        var imported = Now.AddDays(-30);
        var sounds = new[]
        {
            MakeSound("zeta", uploadedAt: imported),
            MakeSound("alpha", uploadedAt: imported),
            MakeSound("newer", uploadedAt: Now),
        };

        var result = Apply(sounds, new BoardPreferences(Sort: BoardSort.RecentlyUploaded));

        Assert.Equal(["newer", "alpha", "zeta"], Names(result));
    }

    [Fact]
    public void RecentlyPlayed_PutsNeverPlayedLast()
    {
        // A never-played sound has no timestamp at all; it must sink rather than sort as if
        // it were played at the dawn of time in the middle of the list.
        var old = MakeSound("old");
        var fresh = MakeSound("fresh");
        var never = MakeSound("never");

        var stats = new Dictionary<Guid, SoundPlayStats>
        {
            [old.Id] = new(old.Id, 1, 0, 0, Now.AddDays(-5)),
            [fresh.Id] = new(fresh.Id, 1, 0, 0, Now),
        };

        var result = Apply(
            [never, old, fresh], new BoardPreferences(Sort: BoardSort.RecentlyPlayed), stats: stats);

        Assert.Equal(["fresh", "old", "never"], Names(result));
    }

    [Fact]
    public void LongestAndShortest_AreOpposites()
    {
        var sounds = new[]
        {
            MakeSound("short", durationMs: 500),
            MakeSound("long", durationMs: 5000),
            MakeSound("mid", durationMs: 2500),
        };

        Assert.Equal(
            ["long", "mid", "short"],
            Names(Apply(sounds, new BoardPreferences(Sort: BoardSort.Longest))));

        Assert.Equal(
            ["short", "mid", "long"],
            Names(Apply(sounds, new BoardPreferences(Sort: BoardSort.Shortest))));
    }

    [Fact]
    public void Search_StillMatchesNameTagsAndEmojiName()
    {
        // Carried over from the page: you cannot type :kekw: into the box, so matching the
        // custom emoji's name is what makes emoji findable at all.
        var byName = MakeSound("airhorn");
        var byTag = MakeSound("something", tags: "airhorn,loud");
        var unrelated = MakeSound("piano");

        var result = Apply([byName, byTag, unrelated], search: "airhorn");

        Assert.Equal(["airhorn", "something"], Names(result));
    }

    [Fact]
    public void TagFilter_RequiresEverySelectedTag()
    {
        var both = MakeSound("both", tags: "meme,loud");
        var one = MakeSound("one", tags: "meme");

        var result = Apply(
            [both, one], new BoardPreferences(Tags: ["meme", "loud"]));

        Assert.Equal(["both"], Names(result));
    }

    [Fact]
    public void NeverPlayed_KeepsOnlySoundsMissingFromTheStats()
    {
        // The aggregate omits sounds nobody has pressed rather than returning zeros, so
        // "never played" is an absence check. It follows that a clip whose only history is
        // entry sounds counts as never played — surprising, but exactly the settled rule.
        var played = MakeSound("played");
        var never = MakeSound("never");

        var result = Apply(
            [played, never],
            new BoardPreferences(Filters: BoardFilter.NeverPlayed),
            stats: Stats((played, 4)));

        Assert.Equal(["never"], Names(result));
    }

    [Fact]
    public void FavouritesOnly_KeepsOnlyStarredSounds()
    {
        var starred = MakeSound("starred");
        var plain = MakeSound("plain");

        var result = Apply(
            [starred, plain],
            new BoardPreferences(Filters: BoardFilter.FavouritesOnly),
            favorites: new HashSet<Guid> { starred.Id });

        Assert.Equal(["starred"], Names(result));
    }

    [Fact]
    public void Untagged_KeepsOnlySoundsWithNoTagsAtAll()
    {
        var tagged = MakeSound("tagged", tags: "meme");
        var bare = MakeSound("bare");

        var result = Apply([tagged, bare], new BoardPreferences(Filters: BoardFilter.Untagged));

        Assert.Equal(["bare"], Names(result));
    }

    [Fact]
    public void UploaderFilter_KeepsOnlyThatPersonsUploads()
    {
        var mine = MakeSound("mine", uploaderId: 7);
        var theirs = MakeSound("theirs", uploaderId: 9);

        var result = Apply([mine, theirs], new BoardPreferences(UploaderId: 7));

        Assert.Equal(["mine"], Names(result));
    }

    [Fact]
    public void EveryFilter_ComposesAsAnd()
    {
        // The one that matters: a viewer can have a search term, tag chips, an uploader and
        // two toggles active at once, and every one of them has to narrow the result.
        var match = MakeSound("airhorn blast", tags: "meme", uploaderId: 7);
        var wrongUploader = MakeSound("airhorn other", tags: "meme", uploaderId: 9);
        var wrongSearch = MakeSound("piano", tags: "meme", uploaderId: 7);
        var wrongTag = MakeSound("airhorn quiet", tags: "calm", uploaderId: 7);
        var notFavourite = MakeSound("airhorn spare", tags: "meme", uploaderId: 7);

        var result = Apply(
            [match, wrongUploader, wrongSearch, wrongTag, notFavourite],
            new BoardPreferences(
                Filters: BoardFilter.FavouritesOnly | BoardFilter.NeverPlayed,
                UploaderId: 7,
                Tags: ["meme"]),
            search: "airhorn",
            favorites: new HashSet<Guid> { match.Id, wrongUploader.Id, wrongSearch.Id, wrongTag.Id });

        Assert.Equal(["airhorn blast"], Names(result));
    }

    [Fact]
    public void NoFiltersSelected_ChangesNothing()
    {
        var sounds = new[] { MakeSound("alpha"), MakeSound("beta") };

        var result = Apply(sounds, new BoardPreferences(Filters: BoardFilter.None));

        Assert.Equal(["alpha", "beta"], Names(result));
    }

    [Fact]
    public void SearchAndTags_ComposeAsAnd()
    {
        var match = MakeSound("airhorn blast", tags: "meme");
        var wrongTag = MakeSound("airhorn quiet", tags: "calm");
        var wrongName = MakeSound("piano", tags: "meme");

        var result = Apply(
            [match, wrongTag, wrongName],
            new BoardPreferences(Tags: ["meme"]),
            search: "airhorn");

        Assert.Equal(["airhorn blast"], Names(result));
    }
}
