namespace SemperSounds.Core.Audio;

/// <summary>
/// Decides when the bot should appear to be speaking, so Discord's green ring tracks
/// actual audio instead of staying lit for the whole session.
/// </summary>
/// <remarks>
/// Pure state, no I/O: the caller performs the websocket update. The flag is lowered only
/// after a short run of silence, because rapid-fire sounds leave gaps between clips and
/// toggling across each one would spam a rate-limited payload for no visible benefit.
/// </remarks>
public sealed class SpeakingGate(TimeSpan linger, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private DateTimeOffset _silentSince = DateTimeOffset.MaxValue;

    /// <summary>
    /// Whether the flag is currently raised. Settable so the caller can align it with the
    /// state Discord was actually left in — notably after joining, which raises it.
    /// </summary>
    public bool IsSpeaking { get; set; }

    /// <summary>
    /// Feeds one frame's worth of "was there audio" and returns the flag to send, or null
    /// when nothing needs sending.
    /// </summary>
    public bool? Update(bool hasAudio)
    {
        if (hasAudio)
        {
            _silentSince = DateTimeOffset.MaxValue;

            if (IsSpeaking)
            {
                return null;
            }

            IsSpeaking = true;
            return true;
        }

        if (!IsSpeaking)
        {
            return null;
        }

        var now = _time.GetUtcNow();

        if (_silentSince == DateTimeOffset.MaxValue)
        {
            _silentSince = now;
            return null;
        }

        if (now - _silentSince < linger)
        {
            return null;
        }

        IsSpeaking = false;
        _silentSince = DateTimeOffset.MaxValue;
        return false;
    }

    /// <summary>Forgets any pending silence, for use when a connection is torn down.</summary>
    public void Reset(bool speaking)
    {
        IsSpeaking = speaking;
        _silentSince = DateTimeOffset.MaxValue;
    }
}
