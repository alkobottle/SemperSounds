namespace SemperSounds.Web.Services;

/// <param name="SoundName">Name at the time of playing, so the entry reads correctly later.</param>
public readonly record struct SoundPlayedNotification(
    Guid SoundId, string SoundName, ulong UserId, string UserName, DateTimeOffset PlayedAt);

/// <summary>
/// In-process pub/sub between the Discord services and the Blazor circuits.
/// A singleton event bus is enough here: everything runs in one process, and the
/// Blazor circuit already provides the browser push.
/// </summary>
public sealed class SoundboardEvents
{
    /// <summary>A sound started playing.</summary>
    public event Action<SoundPlayedNotification>? SoundPlayed;

    /// <summary>Someone joined, left or moved between voice channels.</summary>
    public event Action? VoiceStateChanged;

    /// <summary>The bot connected to or disconnected from a voice channel.</summary>
    public event Action? ConnectionChanged;

    /// <summary>The library changed (upload or delete).</summary>
    public event Action? LibraryChanged;

    /// <summary>The set of currently sounding clips changed.</summary>
    public event Action? PlaybackChanged;

    public void RaiseSoundPlayed(SoundPlayedNotification notification) => SoundPlayed?.Invoke(notification);
    public void RaiseVoiceStateChanged() => VoiceStateChanged?.Invoke();
    public void RaiseConnectionChanged() => ConnectionChanged?.Invoke();
    public void RaiseLibraryChanged() => LibraryChanged?.Invoke();
    public void RaisePlaybackChanged() => PlaybackChanged?.Invoke();
}
