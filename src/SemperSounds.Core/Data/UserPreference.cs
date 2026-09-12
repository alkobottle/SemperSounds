namespace SemperSounds.Core.Data;

/// <summary>
/// One opaque blob of settings belonging to one user, addressed by a key.
/// </summary>
/// <remarks>
/// The value is deliberately not modelled. Every setting this holds is already serialised by
/// the layer that owns it — the board's by <c>BoardPreferencesJson</c>, which knows how to
/// read a shape written by a newer build and never throws on rubbish — and duplicating that
/// as columns would mean a migration every time a checkbox is added, plus a second place for
/// the two representations to disagree. Nothing queries inside a preference; the only access
/// pattern is "give me this user's blob for this key".
///
/// Keyed on (<see cref="UserId"/>, <see cref="Key"/>) rather than one row per user so a
/// future setting that has nothing to do with the board can share the table without
/// widening it.
/// </remarks>
public sealed class UserPreference
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required ulong UserId { get; set; }

    /// <summary>Which set of settings this is; see <see cref="Keys"/>.</summary>
    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The known keys. Constants rather than free strings: a typo would not fail, it would
    /// silently store a preference nothing ever reads back.
    /// </summary>
    public static class Keys
    {
        /// <summary>Sort, filters, uploader, tags and match mode for <c>/board</c>.</summary>
        public const string Board = "board";
    }

    /// <summary>
    /// Longest value accepted. The board's blob is well under a kilobyte even with every tag
    /// selected; the cap is here so a bug in a caller cannot write an unbounded row.
    /// </summary>
    public const int MaxValueLength = 4096;

    public const int MaxKeyLength = 64;
}
