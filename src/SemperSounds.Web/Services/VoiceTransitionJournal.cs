namespace SemperSounds.Web.Services;

public enum VoiceTransition
{
    /// <summary>Nothing about the user's channel changed — a mute, deafen or camera update.</summary>
    None = 0,
    Joined = 1,
    Moved = 2,
    Left = 3,
}

/// <param name="FromChannelId">Where they were. Null for <see cref="VoiceTransition.Joined"/>.</param>
/// <param name="ToChannelId">Where they are now. Null for <see cref="VoiceTransition.Left"/>.</param>
public readonly record struct VoiceMovement(
    VoiceTransition Kind, ulong UserId, ulong? FromChannelId, ulong? ToChannelId)
{
    /// <summary>Joined or moved — the two transitions an entry sound cares about.</summary>
    public bool IsArrival => Kind is VoiceTransition.Joined or VoiceTransition.Moved;

    public static VoiceMovement Nothing(ulong userId) => new(VoiceTransition.None, userId, null, null);
}

/// <summary>
/// Turns the stream of VOICE_STATE_UPDATE payloads into "who arrived where", by
/// remembering which channel each user was in last.
/// </summary>
/// <remarks>
/// <para>
/// Discord sends a voice state update for muting, deafening and enabling video as well as
/// for actually moving, and every one of them carries the user's current channel. Only a
/// comparison against the previous channel separates an arrival from a microphone toggle.
/// </para>
/// <para>
/// This does not contradict <see cref="VoiceStateTracker"/>'s rule against keeping a second
/// copy of voice state. That rule is about <em>current</em> state, which NetCord owns and a
/// duplicate could only disagree with. What is stored here is the <em>previous</em> channel,
/// which the cache structurally cannot answer: NetCord overwrites each entry in place and
/// the event payload carries only the new state. Nothing reads this journal to ask where
/// someone is now — that question still goes to <see cref="VoiceStateTracker"/> alone.
/// </para>
/// <para>
/// Seeded wholesale from GUILD_CREATE, so a fresh IDENTIFY re-primes it and the first update
/// after a reconnect is not mistaken for everyone arriving at once. Guarded by a lock: the
/// gateway is the only writer, but nothing in this codebase has verified that NetCord
/// serialises its dispatch, and at this rate the lock costs nothing.
/// </para>
/// </remarks>
public sealed class VoiceTransitionJournal
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ulong, ulong> _channelByUser = [];
    private bool _seeded;

    /// <summary>
    /// False until GUILD_CREATE has primed the journal. Until then no observation can be
    /// called an arrival, because there is nothing truthful to compare against.
    /// </summary>
    public bool IsSeeded
    {
        get
        {
            lock (_gate)
            {
                return _seeded;
            }
        }
    }

    /// <summary>Replaces the journal with the guild's current occupants.</summary>
    public void Seed(IEnumerable<(ulong UserId, ulong ChannelId)> occupants)
    {
        lock (_gate)
        {
            _channelByUser.Clear();

            foreach (var (userId, channelId) in occupants)
            {
                _channelByUser[userId] = channelId;
            }

            _seeded = true;
        }
    }

    /// <summary>Folds one voice state update in and reports what actually changed.</summary>
    /// <param name="channelId">The channel they are in now; null once they have left voice.</param>
    public VoiceMovement Observe(ulong userId, ulong? channelId)
    {
        lock (_gate)
        {
            var known = _channelByUser.TryGetValue(userId, out var previous);

            if (channelId is not { } destination)
            {
                _channelByUser.Remove(userId);

                return known && _seeded
                    ? new VoiceMovement(VoiceTransition.Left, userId, previous, null)
                    : VoiceMovement.Nothing(userId);
            }

            _channelByUser[userId] = destination;

            if (!_seeded)
            {
                return VoiceMovement.Nothing(userId);
            }

            if (!known)
            {
                return new VoiceMovement(VoiceTransition.Joined, userId, null, destination);
            }

            return previous == destination
                ? VoiceMovement.Nothing(userId)
                : new VoiceMovement(VoiceTransition.Moved, userId, previous, destination);
        }
    }
}
