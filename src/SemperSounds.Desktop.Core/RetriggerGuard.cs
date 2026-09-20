using System.Collections.Concurrent;

namespace SemperSounds.Desktop.Core;

/// <summary>
/// Keeps the client from asking for a clip it already knows is sounding.
/// </summary>
/// <remarks>
/// <para>
/// The server refuses a re-trigger as well, and that check is the authoritative one: it holds
/// the mixer and it answers for every client at once. This is not a replacement for it. It is
/// here because the round trip is waste — a hammered key would otherwise send a hub call per
/// press, every one of them to be told no, down a connection the hotkey path wants kept quiet.
/// </para>
/// <para>
/// A duration timer rather than a flag the server clears, because nothing tells the client when
/// a clip actually ended. Approximately right and free beats exactly right and chatty, and the
/// server is there to catch the cases where the approximation is wrong.
/// </para>
/// </remarks>
public sealed class RetriggerGuard(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _playingUntil = new();

    /// <summary>
    /// How long to hold a clip whose length is not known.
    /// </summary>
    /// <remarks>
    /// Reached before the library has loaded. Guarding for a moment is much better than not at
    /// all: with no guard this would be the one case where hammering still worked, and it is
    /// the first press of every session.
    /// </remarks>
    public static readonly TimeSpan UnknownDurationGuard = TimeSpan.FromSeconds(1);

    /// <summary>
    /// True when the clip may be played, and marks it as sounding for its own length.
    /// </summary>
    /// <remarks>
    /// A refusal deliberately does not push the window out. Leaning on a key would otherwise
    /// keep renewing the block for as long as the key was held, and the clip would never become
    /// playable again.
    /// </remarks>
    public bool TryBegin(Guid soundId, TimeSpan? duration)
    {
        var now = _time.GetUtcNow();

        if (_playingUntil.TryGetValue(soundId, out var until) && now < until)
        {
            return false;
        }

        var hold = duration is { } known && known > TimeSpan.Zero ? known : UnknownDurationGuard;
        _playingUntil[soundId] = now.Add(hold);
        return true;
    }

    /// <summary>Forgets a clip that never actually started, so the next press is not blocked.</summary>
    public void Clear(Guid soundId) => _playingUntil.TryRemove(soundId, out _);

    /// <summary>
    /// Forgets everything, for when all playback has been stopped.
    /// </summary>
    /// <remarks>
    /// Without this, pressing the panic key and then immediately pressing a sound key would be
    /// refused for the remainder of a clip that is no longer playing.
    /// </remarks>
    public void Reset() => _playingUntil.Clear();
}
