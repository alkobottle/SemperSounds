namespace SemperSounds.Core.Data;

/// <summary>
/// The sound played when one user walks into the voice channel the bot is sitting in.
/// One row per user, enforced by a unique index rather than only in code.
/// </summary>
public sealed class EntrySound
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required ulong UserId { get; set; }

    public required Guid SoundId { get; set; }

    /// <summary>
    /// Navigation to the sound. A real foreign key with cascade delete, like
    /// <see cref="Favorite"/>: an assignment pointing at a deleted sound is a dangling
    /// pointer rather than history worth keeping.
    /// </summary>
    public Sound? Sound { get; set; }

    /// <summary>
    /// The user's own switch. Keeps the assignment so turning it back on costs one click,
    /// which is why this is not simply deleting the row.
    /// </summary>
    public bool IsMuted { get; set; }

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}
