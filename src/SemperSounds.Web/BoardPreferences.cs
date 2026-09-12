using SemperSounds.Core.Data;
using SemperSounds.Core.Sounds;
using SemperSounds.Core.Statistics;

namespace SemperSounds.Web;

/// <remarks>
/// Values are explicit and persisted by name, never by number: these end up in browser
/// storage, and renumbering would silently repoint somebody's saved choice at a different sort.
/// </remarks>
public enum BoardSort
{
    Name = 0,
    MostPlayed = 1,
    RecentlyUploaded = 2,
    RecentlyPlayed = 3,
    Longest = 4,
    Shortest = 5,
}

/// <remarks>Flags, so the toggles compose; persisted by name for the same reason as
/// <see cref="BoardSort"/>.</remarks>
[Flags]
public enum BoardFilter
{
    None = 0,
    NeverPlayed = 1,
    FavouritesOnly = 2,
    Untagged = 4,
}

/// <remarks>How the active filters combine. Persisted by name, as the other two are.</remarks>
public enum BoardMatch
{
    All = 0,
    Any = 1,
}

/// <param name="Tags">Under <see cref="BoardMatch.All"/> every selected tag must be present;
/// under <see cref="BoardMatch.Any"/> one is enough.</param>
/// <param name="UploaderId">Null means any uploader.</param>
/// <param name="Match">Defaulted to <see cref="BoardMatch.All"/>, which is how the board
/// behaved before the toggle existed — so a stored preference without it reads as unchanged.</param>
public sealed record BoardPreferences(
    BoardSort Sort = BoardSort.Name,
    BoardFilter Filters = BoardFilter.None,
    ulong? UploaderId = null,
    IReadOnlyList<string>? Tags = null,
    BoardMatch Match = BoardMatch.All)
{
    /// <summary>Drops every filter, keeping how the board is ordered and combined.</summary>
    /// <remarks>
    /// Sort and <see cref="Match"/> survive deliberately: neither hides a sound, so neither is
    /// what somebody means by "clear the filters", and throwing away a chosen sort as a side
    /// effect is the kind of thing that makes people stop trusting the button. Kept here
    /// rather than written inline in the page so it is reachable from a test.
    /// </remarks>
    public BoardPreferences ClearFilters() =>
        this with { Filters = BoardFilter.None, UploaderId = null, Tags = null };
}

/// <summary>
/// Turns the library plus the viewer's choices into the list of tiles to render.
/// </summary>
/// <remarks>
/// Deliberately a pure static function rather than logic inside <c>Home.razor</c>. There is no
/// bUnit in this solution, so anything left in the page is untestable — and every rule here
/// (tie-breaking, what sinks to the bottom, how filters compose) is exactly the kind of thing
/// that breaks quietly.
/// </remarks>
public static class BoardView
{
    public static List<Sound> Apply(
        IEnumerable<Sound> sounds,
        BoardPreferences preferences,
        string search,
        IReadOnlySet<Guid> favorites,
        IReadOnlyDictionary<Guid, SoundPlayStats> stats)
    {
        var query = Filter(sounds, preferences, search, favorites, stats);
        return [.. Sort(query, preferences.Sort, stats)];
    }

    /// <remarks>
    /// Built as a list of clauses rather than chained <c>Where</c> calls because the viewer
    /// chooses how they compose. Only the criteria actually in use contribute one — an empty
    /// search box or an unselected chip has to be absent, not a clause that happens to be
    /// true, or <see cref="BoardMatch.Any"/> would match the whole library the moment any one
    /// dimension was left alone.
    /// </remarks>
    private static IEnumerable<Sound> Filter(
        IEnumerable<Sound> sounds,
        BoardPreferences preferences,
        string search,
        IReadOnlySet<Guid> favorites,
        IReadOnlyDictionary<Guid, SoundPlayStats> stats)
    {
        var clauses = new List<Func<Sound, bool>>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            // One clause, not three: the name/tag/emoji arms are how a single search term is
            // evaluated, and they stay an OR in both modes. Matching the custom emoji's name
            // is what makes emoji findable at all — you cannot type :kekw: into the box, but
            // you can type "kekw".
            clauses.Add(s =>
                s.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                s.Tags.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                EmojiMatches(s.Emoji, term));
        }

        if (preferences.Tags is { Count: > 0 } tags)
        {
            // The chip row is itself multi-valued, so the mode reaches inside it: All wants
            // every selected tag present, Any wants one of them.
            clauses.Add(preferences.Match == BoardMatch.All
                ? s => tags.All(tag => s.TagList.Contains(tag))
                : s => tags.Any(tag => s.TagList.Contains(tag)));
        }

        if (preferences.UploaderId is { } uploaderId)
        {
            clauses.Add(s => s.UploaderId == uploaderId);
        }

        if (preferences.Filters.HasFlag(BoardFilter.FavouritesOnly))
        {
            clauses.Add(s => favorites.Contains(s.Id));
        }

        if (preferences.Filters.HasFlag(BoardFilter.Untagged))
        {
            clauses.Add(s => !s.TagList.Any());
        }

        // The aggregate leaves out sounds nobody has pressed rather than returning zeros,
        // so "never played" is an absence check. A clip whose only history is entry sounds
        // therefore counts as never played, which follows from counting presses only.
        if (preferences.Filters.HasFlag(BoardFilter.NeverPlayed))
        {
            clauses.Add(s => !stats.ContainsKey(s.Id));
        }

        // An untouched board is the common case and must show everything. Falling through to
        // the Any branch would not: Any over an empty list is false, so the board would come
        // up blank with no filter set to explain it.
        if (clauses.Count == 0) return sounds;

        return preferences.Match == BoardMatch.Any
            ? sounds.Where(s => clauses.Any(clause => clause(s)))
            : sounds.Where(s => clauses.All(clause => clause(s)));
    }

    /// <remarks>
    /// Every arm ends in the same name tiebreak, and that is not defensive padding. The bulk
    /// importer gives every clip it creates the same <c>UploadedAt</c>, so "recently uploaded"
    /// is mostly ties; and most of the library has never been played, so "most played" is one
    /// large tie along the bottom. Without it the order there is arbitrary and shifts between
    /// renders. The Name arm is spelled out rather than relying on the order the library
    /// happens to arrive in.
    /// </remarks>
    private static IEnumerable<Sound> Sort(IEnumerable<Sound> query, BoardSort sort, IReadOnlyDictionary<Guid, SoundPlayStats> stats) =>
        sort switch
        {
            BoardSort.MostPlayed => query
                .OrderByDescending(s => Plays(stats, s.Id))
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),

            BoardSort.RecentlyUploaded => query
                .OrderByDescending(s => s.UploadedAt)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),

            // Never played has no timestamp, mapped to the earliest possible instant so it
            // lands at the bottom. Ordering nulls descending would put them last anyway; this
            // says so out loud rather than resting on that.
            BoardSort.RecentlyPlayed => query
                .OrderByDescending(s => LastPlayed(stats, s.Id) ?? DateTimeOffset.MinValue)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),

            BoardSort.Longest => query
                .OrderByDescending(s => s.DurationMs)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),

            BoardSort.Shortest => query
                .OrderBy(s => s.DurationMs)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),

            _ => query.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
        };

    private static int Plays(IReadOnlyDictionary<Guid, SoundPlayStats> stats, Guid soundId) =>
        stats.TryGetValue(soundId, out var found) ? found.Plays : 0;

    private static DateTimeOffset? LastPlayed(IReadOnlyDictionary<Guid, SoundPlayStats> stats, Guid soundId) =>
        stats.TryGetValue(soundId, out var found) ? found.LastPlayedAt : null;

    private static bool EmojiMatches(string stored, string term) =>
        SoundEmoji.TryParse(stored, out var emoji) &&
        emoji.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase);
}
