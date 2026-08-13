using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;

namespace SemperSounds.Core.EntrySounds;

public readonly record struct EntrySoundResult(bool IsSuccess, string Error)
{
    public static EntrySoundResult Ok => new(true, string.Empty);
    public static EntrySoundResult Fail(string error) => new(false, error);
}

/// <summary>
/// Reads and writes who has which entry sound. Scoped, alongside the other libraries.
/// </summary>
public sealed class EntrySoundLibrary(SoundboardDbContext db)
{
    /// <summary>
    /// The server-wide settings. The row is seeded by the model, so this never has to
    /// invent defaults for a missing one.
    /// </summary>
    public async Task<EntrySoundSettingsSnapshot> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await db.EntrySoundSettings
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        return new EntrySoundSettingsSnapshot(
            settings.IsEnabled,
            settings.SnoozedUntil,
            settings.VolumePercent,
            settings.PerUserCooldownSeconds,
            settings.MaxDurationMs);
    }

    /// <summary>One user's assignment, with the sound loaded for display.</summary>
    public Task<EntrySound?> FindAsync(ulong userId, CancellationToken cancellationToken = default) =>
        db.EntrySounds
            .AsNoTracking()
            .Include(entry => entry.Sound)
            .SingleOrDefaultAsync(entry => entry.UserId == userId, cancellationToken);

    /// <summary>Every assignment, for the overview of who walks in to what.</summary>
    public Task<List<EntrySound>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.EntrySounds
            .AsNoTracking()
            .Include(entry => entry.Sound)
            .OrderBy(entry => entry.AssignedAt)
            .ToListAsync(cancellationToken);

    /// <summary>Picks someone's entry sound, replacing whatever they had before.</summary>
    public async Task<EntrySoundResult> AssignAsync(
        ulong userId, Guid soundId, CancellationToken cancellationToken = default)
    {
        var sound = await db.Sounds
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == soundId, cancellationToken);

        if (sound is null)
        {
            return EntrySoundResult.Fail("That sound no longer exists.");
        }

        var settings = await GetSettingsAsync(cancellationToken);

        // Checked here rather than at playback, so tightening the cap later never
        // silently unassigns someone who picked while a longer clip was allowed.
        if (sound.DurationMs > settings.MaxDurationMs)
        {
            return EntrySoundResult.Fail(
                $"Entry sounds have to be {settings.MaxDurationMs / 1000.0:0.#}s or shorter — " +
                $"{sound.Name} is {sound.DurationMs / 1000.0:0.#}s.");
        }

        var existing = await db.EntrySounds
            .SingleOrDefaultAsync(entry => entry.UserId == userId, cancellationToken);

        if (existing is null)
        {
            db.EntrySounds.Add(new EntrySound { UserId = userId, SoundId = soundId });
        }
        else
        {
            // IsMuted is deliberately left alone: changing your pick is not a request to
            // start making noise again.
            existing.SoundId = soundId;
            existing.AssignedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return EntrySoundResult.Ok;
    }

    public async Task ClearAsync(ulong userId, CancellationToken cancellationToken = default)
    {
        var existing = await db.EntrySounds
            .SingleOrDefaultAsync(entry => entry.UserId == userId, cancellationToken);

        if (existing is null)
        {
            return;
        }

        db.EntrySounds.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The user's own switch. Keeps the assignment either way.</summary>
    public async Task SetMutedAsync(
        ulong userId, bool muted, CancellationToken cancellationToken = default)
    {
        var existing = await db.EntrySounds
            .SingleOrDefaultAsync(entry => entry.UserId == userId, cancellationToken);

        if (existing is null || existing.IsMuted == muted)
        {
            return;
        }

        existing.IsMuted = muted;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Everyone currently blocked, for marking up a list of all assignments.</summary>
    public async Task<HashSet<ulong>> GetBlockedUserIdsAsync(CancellationToken cancellationToken = default) =>
        [.. await db.EntrySoundBlocks
            .AsNoTracking()
            .Select(block => block.UserId)
            .ToListAsync(cancellationToken)];

    /// <summary>The administrator's block on this user, or null when they are not blocked.</summary>
    public Task<EntrySoundBlock?> FindBlockAsync(
        ulong userId, CancellationToken cancellationToken = default) =>
        db.EntrySoundBlocks
            .AsNoTracking()
            .SingleOrDefaultAsync(block => block.UserId == userId, cancellationToken);
}
