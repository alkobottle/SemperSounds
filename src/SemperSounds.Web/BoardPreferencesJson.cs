using System.Text.Json;
using System.Text.Json.Serialization;

namespace SemperSounds.Web;

/// <summary>
/// Reads and writes the viewer's board preferences as the single JSON blob kept in browser
/// storage.
/// </summary>
/// <remarks>
/// <para>
/// One versioned key rather than a field each: the whole set moves, or is discarded, in one
/// step. The version lives in the key name so an incompatible future shape is a clean miss
/// that falls back to defaults, rather than a parse failure on every load forever.
/// </para>
/// <para>
/// <see cref="Deserialize"/> never throws. It is called from <c>OnAfterRenderAsync</c>, where
/// an escaping exception takes the circuit down — so anything unreadable, from absent storage
/// to a half-written value to a preference saved by a newer build, has to degrade to today's
/// board instead.
/// </para>
/// </remarks>
public static class BoardPreferencesJson
{
    public const string StorageKey = "sempersounds.board.v1";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <param name="UploaderId">
    /// A string, deliberately. Discord snowflakes exceed Number.MAX_SAFE_INTEGER, so stored as
    /// a JSON number any JSON.parse in the browser rounds it silently and the uploader filter
    /// then matches nobody, with nothing anywhere to explain the empty board.
    /// </param>
    /// <param name="Sort">Stored by name: stored JSON outlives the enum, and by number a
    /// reordered member would repoint an existing choice at a different sort.</param>
    private sealed record Stored(
        string? Sort = null,
        string[]? Filters = null,
        string? UploaderId = null,
        string[]? Tags = null);

    public static string Serialize(BoardPreferences preferences) =>
        JsonSerializer.Serialize(
            new Stored(
                preferences.Sort.ToString(),
                [.. Enum.GetValues<BoardFilter>()
                    .Where(flag => flag != BoardFilter.None && preferences.Filters.HasFlag(flag))
                    .Select(flag => flag.ToString())],
                preferences.UploaderId?.ToString(),
                preferences.Tags is { Count: > 0 } tags ? [.. tags] : null),
            Options);

    public static BoardPreferences Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new BoardPreferences();
        }

        Stored? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Stored>(json, Options);
        }
        catch (JsonException)
        {
            return new BoardPreferences();
        }

        if (stored is null)
        {
            return new BoardPreferences();
        }

        var filters = BoardFilter.None;
        foreach (var name in stored.Filters ?? [])
        {
            // Unknown names are skipped rather than discarding the rest, so a preference
            // written by a newer build degrades to the part this one understands.
            if (Enum.TryParse<BoardFilter>(name, out var filter))
            {
                filters |= filter;
            }
        }

        return new BoardPreferences(
            Enum.TryParse<BoardSort>(stored.Sort, out var sort) ? sort : BoardSort.Name,
            filters,
            ulong.TryParse(stored.UploaderId, out var uploaderId) ? uploaderId : null,
            stored.Tags);
    }
}
