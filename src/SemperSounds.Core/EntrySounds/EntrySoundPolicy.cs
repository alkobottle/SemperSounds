namespace SemperSounds.Core.EntrySounds;

/// <summary>Why an arrival did not produce a sound. Ordered as the policy checks them.</summary>
public enum EntrySoundRefusal
{
    None = 0,
    BotNotInChannel = 1,
    Disabled = 2,
    Snoozed = 3,
    NobodyToHearIt = 4,
    NoAssignment = 5,
    SelfMuted = 6,
    Blocked = 7,
    Cooldown = 8,
}

/// <param name="OtherHumansInChannel">
/// Humans in the channel besides the arriving user and the bot. Null when the guild cache
/// could not answer, which is not the same as zero.
/// </param>
public readonly record struct EntrySoundRequest(
    ulong UserId,
    ulong ChannelId,
    ulong? BotChannelId,
    int? OtherHumansInChannel,
    Guid? AssignedSoundId,
    bool IsSelfMuted,
    bool IsBlocked,
    DateTimeOffset? LastEntryPlayedAt);

public readonly record struct EntrySoundDecision(Guid SoundId, EntrySoundRefusal Refusal)
{
    public bool ShouldPlay => Refusal == EntrySoundRefusal.None;

    public static EntrySoundDecision Refuse(EntrySoundRefusal refusal) => new(Guid.Empty, refusal);
}

/// <summary>
/// Decides whether someone walking into a voice channel gets their entry sound.
/// </summary>
/// <remarks>
/// Pure state, no I/O and no Discord types, so every rule is testable without a gateway
/// connection — the same treatment as <c>IdleTimer</c> and <c>SpeakingGate</c>.
/// </remarks>
public sealed class EntrySoundPolicy(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public EntrySoundDecision Decide(EntrySoundSettingsSnapshot settings, in EntrySoundRequest request)
    {
        // First, because it is both the commonest answer and the only one the caller can
        // reach without touching the database. The bot never follows anyone: if it is not
        // already sitting in that channel, nothing happens.
        if (request.BotChannelId != request.ChannelId)
        {
            return EntrySoundDecision.Refuse(EntrySoundRefusal.BotNotInChannel);
        }

        if (!settings.IsEnabled)
        {
            return EntrySoundDecision.Refuse(EntrySoundRefusal.Disabled);
        }

        // Compared, never cleared: a lapsed snooze needs no write and no timer.
        if (settings.SnoozedUntil is { } until && until > _time.GetUtcNow())
        {
            return EntrySoundDecision.Refuse(EntrySoundRefusal.Snoozed);
        }

        // Null means the cache could not answer. Refusing on unknown occupancy is the
        // opposite of IdleTimer's "unknown means occupied", and deliberately so: both
        // decline to act on a guess, and the action here is playing rather than leaving.
        if (request.OtherHumansInChannel is null or < 1)
        {
            return EntrySoundDecision.Refuse(EntrySoundRefusal.NobodyToHearIt);
        }

        if (request.AssignedSoundId is not { } soundId)
        {
            return EntrySoundDecision.Refuse(EntrySoundRefusal.NoAssignment);
        }

        if (request.IsSelfMuted)
        {
            return EntrySoundDecision.Refuse(EntrySoundRefusal.SelfMuted);
        }

        if (request.IsBlocked)
        {
            return EntrySoundDecision.Refuse(EntrySoundRefusal.Blocked);
        }

        // Zero disables the cooldown, matching SoundboardOptions.PerUserCooldownSeconds.
        if (settings.PerUserCooldownSeconds > 0 &&
            request.LastEntryPlayedAt is { } last &&
            _time.GetUtcNow() - last < TimeSpan.FromSeconds(settings.PerUserCooldownSeconds))
        {
            return EntrySoundDecision.Refuse(EntrySoundRefusal.Cooldown);
        }

        return new EntrySoundDecision(soundId, EntrySoundRefusal.None);
    }
}
