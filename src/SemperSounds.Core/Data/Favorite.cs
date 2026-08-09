namespace SemperSounds.Core.Data;

/// <summary>
/// One user's shortcut to a sound. The first per-user state in the app — the library, the
/// play log and playback are all shared.
/// </summary>
public sealed class Favorite
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required ulong UserId { get; set; }

    public required Guid SoundId { get; set; }

    /// <summary>
    /// Navigation to the sound. Unlike <see cref="ActivityLogEntry"/> this is a real
    /// foreign key with cascade delete: a play that happened is history and must outlive
    /// the sound, but a favourite pointing at a deleted sound is only a dangling pointer.
    /// </summary>
    public Sound? Sound { get; set; }

    /// <summary>Keyboard slot, 1-based. Contiguous within a user.</summary>
    public int Slot { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
