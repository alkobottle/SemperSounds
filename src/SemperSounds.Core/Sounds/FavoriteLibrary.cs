using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;

namespace SemperSounds.Core.Sounds;

/// <param name="IsFavorited">State after the toggle.</param>
/// <param name="Error">Why the toggle was refused, empty when it succeeded.</param>
public readonly record struct FavoriteToggleResult(bool IsFavorited, string Error)
{
    public static FavoriteToggleResult Added => new(true, string.Empty);
    public static FavoriteToggleResult Removed => new(false, string.Empty);
    public static FavoriteToggleResult Refused(string error) => new(false, error);
}

/// <summary>
/// Each user's own shortlist of sounds, numbered 1..<see cref="MaxSlots"/> for the keyboard.
/// </summary>
/// <remarks>
/// Separate from <see cref="SoundLibrary"/> on purpose: that type already owns uploading,
/// deletion, tags, the play log and PCM reading, and favourites share none of it.
/// </remarks>
public sealed class FavoriteLibrary(SoundboardDbContext db)
{
    /// <summary>
    /// How many favourites one user may hold. Nine is roughly the limit of what fingers
    /// reach without looking, and it keeps the shortcuts to a single digit.
    /// </summary>
    public const int MaxSlots = 9;

    public Task<List<Favorite>> GetForUserAsync(ulong userId, CancellationToken cancellationToken = default) =>
        db.Favorites.AsNoTracking()
            .Include(f => f.Sound)
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Slot)
            .ToListAsync(cancellationToken);

    /// <summary>The user's favourited sound IDs, for rendering star state on tiles.</summary>
    public async Task<HashSet<Guid>> GetFavoritedSoundIdsAsync(
        ulong userId, CancellationToken cancellationToken = default) =>
        [.. await db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId)
            .Select(f => f.SoundId)
            .ToListAsync(cancellationToken)];

    public async Task<FavoriteToggleResult> ToggleAsync(
        ulong userId, Guid soundId, CancellationToken cancellationToken = default)
    {
        var existing = await db.Favorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.SoundId == soundId, cancellationToken);

        if (existing is not null)
        {
            db.Favorites.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            await CompactAsync(userId, cancellationToken);
            return FavoriteToggleResult.Removed;
        }

        var used = await db.Favorites
            .Where(f => f.UserId == userId)
            .Select(f => f.Slot)
            .ToListAsync(cancellationToken);

        if (used.Count >= MaxSlots)
        {
            // Refused rather than evicting: the whole value of a favourite is that its key
            // stays where the user put it.
            return FavoriteToggleResult.Refused(
                $"You already have {MaxSlots} favourites. Remove one before adding another.");
        }

        db.Favorites.Add(new Favorite
        {
            UserId = userId,
            SoundId = soundId,
            Slot = LowestFreeSlot(used),
        });

        await db.SaveChangesAsync(cancellationToken);
        return FavoriteToggleResult.Added;
    }

    /// <summary>Swaps a favourite with its neighbour, so the user controls which key it gets.</summary>
    public async Task MoveAsync(ulong userId, Guid soundId, bool up, CancellationToken cancellationToken = default)
    {
        var ordered = await db.Favorites
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Slot)
            .ToListAsync(cancellationToken);

        var index = ordered.FindIndex(f => f.SoundId == soundId);
        var target = up ? index - 1 : index + 1;

        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return;
        }

        // Park one slot outside the range first: the unique index on (UserId, Slot) would
        // otherwise be violated mid-swap, before SaveChanges reaches the second row.
        var parking = MaxSlots + 1;
        (ordered[index].Slot, ordered[target].Slot, var moved) = (parking, ordered[index].Slot, ordered[target].Slot);
        await db.SaveChangesAsync(cancellationToken);

        ordered[index].Slot = moved;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Closes gaps left by removal, so slots stay contiguous. Without this a user could end
    /// up with keys 1, 3 and 7 and no way to reach 2.
    /// </summary>
    private async Task CompactAsync(ulong userId, CancellationToken cancellationToken)
    {
        var ordered = await db.Favorites
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Slot)
            .ToListAsync(cancellationToken);

        var changed = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Slot != i + 1)
            {
                ordered[i].Slot = i + 1;
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static int LowestFreeSlot(List<int> used)
    {
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            if (!used.Contains(slot))
            {
                return slot;
            }
        }

        return MaxSlots;
    }
}
