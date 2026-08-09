namespace SemperSounds.Core.Audio;

/// <summary>
/// Decides when the bot should drop out of a voice channel nobody is listening in.
/// </summary>
/// <remarks>
/// Pure state, no I/O. The countdown starts on the first empty observation rather than
/// firing immediately, so a momentary gap while someone reconnects does not eject the bot,
/// and anyone rejoining restarts it.
/// </remarks>
public sealed class IdleTimer(TimeSpan timeout, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private DateTimeOffset _emptySince = DateTimeOffset.MaxValue;

    /// <param name="anyoneListening">Whether any human remains in the channel.</param>
    public bool ShouldLeave(bool anyoneListening)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        if (anyoneListening)
        {
            _emptySince = DateTimeOffset.MaxValue;
            return false;
        }

        var now = _time.GetUtcNow();

        if (_emptySince == DateTimeOffset.MaxValue)
        {
            _emptySince = now;
            return false;
        }

        return now - _emptySince >= timeout;
    }

    public void Reset() => _emptySince = DateTimeOffset.MaxValue;
}
