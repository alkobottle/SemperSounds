namespace SemperSounds.Core.Data;

/// <summary>
/// Server-wide entry sound settings. Exactly one row, seeded by the model so no code ever
/// has to handle it being missing.
/// </summary>
/// <remarks>
/// A typed row rather than a key/value bag: every setting here has a real type, and a bag
/// would stringify <see cref="SnoozedUntil"/> — the one value whose type the database
/// genuinely cares about — and push validation out to a parse at every read site.
/// </remarks>
public sealed class EntrySoundSettings
{
    /// <summary>This table holds one row, and this is its id.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// When set in the future, entry sounds stay quiet until then. Stored as an expiry and
    /// only ever compared, so nobody has to remember to switch them back on.
    /// </summary>
    public DateTimeOffset? SnoozedUntil { get; set; }

    /// <summary>Entry sound loudness, so they sit under conversation instead of over it.</summary>
    public int VolumePercent { get; set; } = 70;

    /// <summary>Zero disables the cooldown, matching <c>SoundboardOptions</c>.</summary>
    public int PerUserCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Longest clip that may be picked as an entry sound. Defaults to the board's own limit
    /// so nothing is refused until an administrator deliberately tightens it, and is applied
    /// when assigning rather than when playing, so tightening it never breaks an existing pick.
    /// </summary>
    public int MaxDurationMs { get; set; } = 5000;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ulong? UpdatedByUserId { get; set; }
}
