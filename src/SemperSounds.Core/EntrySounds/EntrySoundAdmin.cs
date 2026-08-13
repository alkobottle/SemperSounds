using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;

namespace SemperSounds.Core.EntrySounds;

/// <summary>
/// The server-wide entry sound controls, and the one place they are authorized.
/// </summary>
/// <remarks>
/// Every mutator takes the acting user's id and checks it first. Following the rule
/// <c>PlaybackService</c> already sets: a hidden or disabled control is a hint, the service
/// is what actually refuses. Permissions are read live rather than from claims, so a role
/// change takes effect without signing out.
/// </remarks>
public sealed class EntrySoundAdmin(
    SoundboardDbContext db,
    IGuildPermissions permissions,
    TimeProvider? timeProvider = null)
{
    /// <summary>Loudest an administrator may set entry sounds to.</summary>
    public const int MaxVolumePercent = 100;

    /// <summary>Bounds on the entry sound length cap, in milliseconds.</summary>
    public const int MinDurationMs = 500;
    public const int MaxDurationMs = 60_000;

    private const string NotAnAdministrator =
        "Only server administrators can change entry sound settings.";

    private const string PermissionsUnknown =
        "Still loading the server's member list — try again in a moment.";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task<EntrySoundResult> SetEnabledAsync(
        ulong actorId, bool enabled, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(actorId, settings => settings.IsEnabled = enabled, cancellationToken);

    /// <summary>Goes quiet until an expiry, rather than needing anyone to switch it back on.</summary>
    public Task<EntrySoundResult> SnoozeAsync(
        ulong actorId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            return Task.FromResult(EntrySoundResult.Fail("Snooze for at least a moment."));
        }

        return UpdateSettingsAsync(
            actorId,
            settings => settings.SnoozedUntil = _time.GetUtcNow() + duration,
            cancellationToken);
    }

    public Task<EntrySoundResult> ResumeAsync(
        ulong actorId, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(actorId, settings => settings.SnoozedUntil = null, cancellationToken);

    public Task<EntrySoundResult> SetVolumeAsync(
        ulong actorId, int percent, CancellationToken cancellationToken = default)
    {
        if (percent is < 0 or > MaxVolumePercent)
        {
            return Task.FromResult(
                EntrySoundResult.Fail($"Volume has to be between 0 and {MaxVolumePercent}%."));
        }

        return UpdateSettingsAsync(
            actorId, settings => settings.VolumePercent = percent, cancellationToken);
    }

    public Task<EntrySoundResult> SetCooldownAsync(
        ulong actorId, int seconds, CancellationToken cancellationToken = default)
    {
        // Zero is meaningful here — it disables the cooldown, matching SoundboardOptions.
        if (seconds < 0)
        {
            return Task.FromResult(EntrySoundResult.Fail("A cooldown cannot be negative."));
        }

        return UpdateSettingsAsync(
            actorId, settings => settings.PerUserCooldownSeconds = seconds, cancellationToken);
    }

    /// <summary>
    /// Changes the cap on entry sound length. Applied when someone picks a sound, so
    /// tightening it never silently unassigns anyone who already chose a longer one.
    /// </summary>
    public Task<EntrySoundResult> SetMaxDurationAsync(
        ulong actorId, int milliseconds, CancellationToken cancellationToken = default)
    {
        if (milliseconds is < MinDurationMs or > MaxDurationMs)
        {
            return Task.FromResult(EntrySoundResult.Fail(
                $"The limit has to be between {MinDurationMs / 1000.0:0.#}s and {MaxDurationMs / 1000.0:0.#}s."));
        }

        return UpdateSettingsAsync(
            actorId, settings => settings.MaxDurationMs = milliseconds, cancellationToken);
    }

    /// <summary>Silences one person's entry sound, leaving everyone else's alone.</summary>
    public async Task<EntrySoundResult> BlockAsync(
        ulong actorId, string actorName, ulong targetUserId, string reason,
        CancellationToken cancellationToken = default)
    {
        if (Refuse(actorId) is { } refusal)
        {
            return refusal;
        }

        var existing = await db.EntrySoundBlocks
            .SingleOrDefaultAsync(block => block.UserId == targetUserId, cancellationToken);

        var trimmed = (reason ?? string.Empty).Trim();

        if (existing is null)
        {
            db.EntrySoundBlocks.Add(new EntrySoundBlock
            {
                UserId = targetUserId,
                Reason = trimmed,
                BlockedByUserId = actorId,
                BlockedByName = actorName,
                BlockedAt = _time.GetUtcNow(),
            });
        }
        else
        {
            // UserId is unique, so blocking again has to update rather than insert.
            existing.Reason = trimmed;
            existing.BlockedByUserId = actorId;
            existing.BlockedByName = actorName;
            existing.BlockedAt = _time.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        return EntrySoundResult.Ok;
    }

    public async Task<EntrySoundResult> UnblockAsync(
        ulong actorId, ulong targetUserId, CancellationToken cancellationToken = default)
    {
        if (Refuse(actorId) is { } refusal)
        {
            return refusal;
        }

        var existing = await db.EntrySoundBlocks
            .SingleOrDefaultAsync(block => block.UserId == targetUserId, cancellationToken);

        if (existing is not null)
        {
            db.EntrySoundBlocks.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        return EntrySoundResult.Ok;
    }

    public Task<List<EntrySoundBlock>> GetBlocksAsync(CancellationToken cancellationToken = default) =>
        db.EntrySoundBlocks
            .AsNoTracking()
            .OrderByDescending(block => block.BlockedAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The refusal for this actor, or null when they may proceed. Unknown permissions are
    /// refused: null means the member cache has not arrived, which is common for a few
    /// seconds after a restart, and failing closed is the only safe reading of it.
    /// </summary>
    private EntrySoundResult? Refuse(ulong actorId) => permissions.IsAdministrator(actorId) switch
    {
        true => null,
        false => EntrySoundResult.Fail(NotAnAdministrator),
        null => EntrySoundResult.Fail(PermissionsUnknown),
    };

    private async Task<EntrySoundResult> UpdateSettingsAsync(
        ulong actorId, Action<EntrySoundSettings> change, CancellationToken cancellationToken)
    {
        if (Refuse(actorId) is { } refusal)
        {
            return refusal;
        }

        var settings = await db.EntrySoundSettings.SingleAsync(cancellationToken);

        change(settings);
        settings.UpdatedAt = _time.GetUtcNow();
        settings.UpdatedByUserId = actorId;

        await db.SaveChangesAsync(cancellationToken);
        return EntrySoundResult.Ok;
    }
}
