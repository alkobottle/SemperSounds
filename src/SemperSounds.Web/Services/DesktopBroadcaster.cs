using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using SemperSounds.Contracts;

namespace SemperSounds.Web.Services;

/// <summary>
/// Pushes bot state to connected desktop clients, and hangs up on revoked ones.
/// </summary>
/// <remarks>
/// <para>
/// Registered as both a singleton and a hosted service, for the reason
/// <see cref="EntrySoundCoordinator"/> documents: a singleton that only subscribes to events
/// is never constructed, and the failure is silent — every tray icon simply stays on whatever
/// it was told when it connected.
/// </para>
/// <para>
/// State is computed per connection rather than broadcast. "Can I press a key right now"
/// depends on which channel <em>you</em> are sitting in, so there is no single answer to send
/// everyone, and one user may have two PCs which both need telling.
/// </para>
/// </remarks>
public sealed class DesktopBroadcaster(
    SoundboardEvents events,
    VoiceStateTracker voiceStates,
    DiscordBotService bot,
    PlaybackService playback,
    IHubContext<DesktopHub, ISoundboardClient> hub,
    ILogger<DesktopBroadcaster> logger,
    TimeProvider? timeProvider = null) : IHostedService, IDisposable
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Subscriber> _connections = new(StringComparer.Ordinal);

    /// <summary>
    /// Set by an event, cleared by the flush that follows it. Voice state is raised once per
    /// member chunk while the guild cache fills after every reconnect, and a guild of any size
    /// arrives as a burst of them — without coalescing, one restart is one push per chunk per
    /// client, all carrying the same answer.
    /// </summary>
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(250);

    private int _flushScheduled;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        events.ConnectionChanged += OnStateChanged;
        events.VoiceStateChanged += OnStateChanged;
        events.LibraryChanged += OnLibraryChanged;
        events.PlaybackChanged += OnPlaybackChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return Task.CompletedTask;
    }

    public void Dispose() => Unsubscribe();

    private void Unsubscribe()
    {
        events.ConnectionChanged -= OnStateChanged;
        events.VoiceStateChanged -= OnStateChanged;
        events.LibraryChanged -= OnLibraryChanged;
        events.PlaybackChanged -= OnPlaybackChanged;
    }

    /// <param name="abort">
    /// Drops the connection. Taken as a callback over the hub's own caller context, because
    /// <see cref="IHubContext{THub, T}"/> can send to a connection but cannot end one.
    /// </param>
    public void Register(string connectionId, ulong userId, Guid? tokenId, Action abort) =>
        _connections[connectionId] = new Subscriber(userId, tokenId, abort) { LastSent = StateFor(userId) };

    public void Unregister(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>
    /// Drops every live connection holding a token that has just been revoked.
    /// </summary>
    /// <remarks>
    /// SignalR authenticates once, at negotiate, and then holds that principal for the life of
    /// the connection — which, with automatic reconnect over a healthy socket, can be weeks.
    /// Without this the revoke button on <c>/devices</c> changes nothing anybody can observe
    /// until the client happens to drop, which is the opposite of what "revoke" means.
    /// </remarks>
    public async Task AbortConnectionsFor(Guid tokenId)
    {
        foreach (var (connectionId, subscriber) in _connections)
        {
            if (subscriber.TokenId != tokenId)
            {
                continue;
            }

            _connections.TryRemove(connectionId, out _);

            try
            {
                // Told first, then dropped: the client shows "not paired" rather than an
                // unexplained disconnection it would otherwise try to reconnect through.
                await hub.Clients.Client(connectionId).StateChanged(BotState.Unknown);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not notify revoked connection {ConnectionId}", connectionId);
            }

            subscriber.Abort();
            logger.LogInformation("Dropped desktop connection {ConnectionId} on a revoked token", connectionId);
        }
    }

    /// <summary>
    /// Raised synchronously on NetCord's gateway thread, so this hands off and returns. An
    /// exception escaping the detached task would be an unobserved fault, hence the catch.
    /// </summary>
    private void OnStateChanged()
    {
        // Only the first event in the window schedules a flush; the rest ride along on it.
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CoalesceWindow, _time);
                Interlocked.Exchange(ref _flushScheduled, 0);
                await FlushAsync();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _flushScheduled, 0);
                logger.LogError(ex, "Failed to push desktop state");
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Pushes what is sounding to everyone.
    /// </summary>
    /// <remarks>
    /// Broadcast rather than computed per connection, unlike bot state: what is playing in the
    /// channel is the same fact for everybody listening to it. Raised by the pump only when the
    /// set actually changes, so this is already as quiet as the audio is.
    /// </remarks>
    private void OnPlaybackChanged() => _ = Task.Run(async () =>
    {
        try
        {
            await hub.Clients.All.NowPlayingChanged(playback.NowPlaying);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not push what is playing");
        }
    }, CancellationToken.None);

    private void OnLibraryChanged() => _ = Task.Run(async () =>
    {
        try
        {
            await hub.Clients.All.LibraryChanged();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to push a library change");
        }
    }, CancellationToken.None);

    /// <summary>
    /// Sends each connection its state, but only when it differs from what that connection was
    /// last told.
    /// </summary>
    /// <remarks>
    /// The diff is not an optimisation, it is the whole reason this is affordable. Voice state
    /// is raised for mutes, deafens and camera toggles as well as for moving, and none of those
    /// can change a <see cref="BotState"/> — so comparing collapses the entire class of event
    /// to zero messages rather than a push per client per microphone tap.
    /// </remarks>
    private async Task FlushAsync()
    {
        foreach (var (connectionId, subscriber) in _connections)
        {
            var state = StateFor(subscriber.UserId);
            if (state == subscriber.LastSent)
            {
                continue;
            }

            subscriber.LastSent = state;

            try
            {
                await hub.Clients.Client(connectionId).StateChanged(state);
            }
            catch (Exception ex)
            {
                // Reachable without a bug: the client may have dropped between the enumeration
                // above and this send.
                logger.LogDebug(ex, "Could not push state to {ConnectionId}", connectionId);
            }
        }
    }

    private BotState StateFor(ulong userId) => BotStateFactory.For(
        bot.IsReady, playback.ConnectedChannelId, playback.ConnectedChannelName, voiceStates.GetChannelOf(userId));

    private sealed class Subscriber(ulong userId, Guid? tokenId, Action abort)
    {
        public ulong UserId { get; } = userId;

        public Guid? TokenId { get; } = tokenId;

        public BotState LastSent { get; set; } = BotState.Unknown;

        public void Abort() => abort();
    }
}
