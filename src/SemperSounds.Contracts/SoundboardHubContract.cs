namespace SemperSounds.Contracts;

/// <summary>Server-to-client pushes on the desktop hub.</summary>
/// <remarks>
/// Kept small on purpose. The desktop app needs to know whether a key press can work and
/// whether its cached library is stale; everything else it asks for when it needs it.
/// </remarks>
public interface ISoundboardClient
{
    Task StateChanged(BotState state);

    /// <summary>A sound was uploaded or deleted — re-read the library, bindings may be stale.</summary>
    Task LibraryChanged();

    /// <summary>
    /// What is sounding in the channel right now, oldest first. Empty when the channel is quiet.
    /// </summary>
    /// <remarks>
    /// Pushed rather than polled: clips are short, so anything asking on a timer would be
    /// mostly wrong and mostly idle at the same time.
    /// </remarks>
    Task NowPlayingChanged(IReadOnlyList<NowPlaying> playing);
}

/// <summary>
/// The client-to-server method names.
/// </summary>
/// <remarks>
/// SignalR resolves these by string, and there is no strongly-typed equivalent of
/// <see cref="ISoundboardClient"/> for the server side. Renaming a hub method therefore breaks
/// nothing at build time and fails at runtime with "Method does not exist" — which reaches the
/// user as a key that simply stopped doing anything. Both ends use these constants so the
/// rename is caught by the compiler instead.
/// </remarks>
public static class DesktopHubMethods
{
    public const string Route = "/hubs/desktop";

    public const string Play = nameof(Play);
    public const string Join = nameof(Join);
    public const string Leave = nameof(Leave);
    public const string StopAll = nameof(StopAll);
    public const string GetSounds = nameof(GetSounds);
    public const string GetState = nameof(GetState);
    public const string GetNowPlaying = nameof(GetNowPlaying);
}
