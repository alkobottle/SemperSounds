using System.Collections.Concurrent;

namespace SemperSounds.Web.Services;

/// <summary>
/// Lets each user through at most once per window.
/// </summary>
/// <remarks>
/// <para>
/// Its own small class rather than more state inside <see cref="PlaybackService"/>, for the
/// same reason <see cref="IdleTimer"/> and <see cref="SpeakingGate"/> are: the rule is worth
/// testing and needs no voice connection to exercise.
/// </para>
/// <para>
/// Deliberately not shared with the play cooldown. That one is a configurable, user-visible
/// pause between clips; this is a floor on how fast the bot can be dragged between channels.
/// Sharing them would mean tuning one silently retunes the other.
/// </para>
/// </remarks>
public sealed class UserThrottle(TimeSpan window, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastAllowed = new();

    /// <summary>
    /// True when this user may proceed, stamping the attempt. False with how long is left.
    /// </summary>
    /// <remarks>
    /// A refusal deliberately does not restamp. Holding a bound key down would otherwise push
    /// the window forward on every repeat, so the wait never counted down and the user would
    /// be locked out for as long as they leant on the key.
    /// </remarks>
    public bool TryAcquire(ulong userId, out TimeSpan remaining)
    {
        var now = _time.GetUtcNow();

        if (_lastAllowed.TryGetValue(userId, out var last) && now - last < window)
        {
            remaining = window - (now - last);
            return false;
        }

        _lastAllowed[userId] = now;
        remaining = TimeSpan.Zero;
        return true;
    }
}
