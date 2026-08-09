namespace SemperSounds.Core.Sounds;

/// <param name="Count">How many sounds carry this tag.</param>
public readonly record struct TagUsage(string Tag, int Count);

/// <summary>
/// Converts between a sound's stored comma-separated tags and a list.
/// </summary>
/// <remarks>
/// Tags are lowercased and de-duplicated on the way in. Case-folding matters more than it
/// looks: the library is only filterable if "Meme" and "meme" are the same tag.
/// </remarks>
public static class TagList
{
    public static IReadOnlyList<string> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return [.. csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)];
    }

    public static string ToCsv(IEnumerable<string> tags) => string.Join(',', Parse(string.Join(',', tags)));
}
