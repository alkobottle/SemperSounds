namespace SemperSounds.Core.Data;

/// <summary>
/// An administrator silencing one person's entry sound. The row existing is the block.
/// </summary>
/// <remarks>
/// Deliberately its own table rather than a flag on <see cref="EntrySound"/>. A block has to
/// outlive the assignment it was aimed at: on the assignment row, clearing and re-picking a
/// sound would launder the block away, and somebody with no assignment yet could not be
/// blocked at all. The administrator's name is denormalized for the same reason
/// <see cref="ActivityLogEntry"/> denormalizes the sound name — the record should still read
/// correctly later.
/// </remarks>
public sealed class EntrySoundBlock
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required ulong UserId { get; set; }

    /// <summary>Shown to the blocked user, so being silent is never a mystery.</summary>
    public string Reason { get; set; } = string.Empty;

    public ulong BlockedByUserId { get; set; }

    public string BlockedByName { get; set; } = string.Empty;

    public DateTimeOffset BlockedAt { get; set; } = DateTimeOffset.UtcNow;
}
