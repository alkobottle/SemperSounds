using System.Collections.Concurrent;
using SemperSounds.Core.EntrySounds;

namespace SemperSounds.Web.Services;

/// <summary>
/// Turns "somebody walked in" into "play their entry sound", if every rule allows it.
/// </summary>
/// <remarks>
/// <para>
/// Registered as both a singleton and a hosted service. A singleton that only subscribes to
/// events is never constructed by the container on its own, and the failure mode is silent —
/// the feature simply never fires — so something has to force it into existence.
/// </para>
/// <para>
/// The decision itself lives in <see cref="EntrySoundPolicy"/>, which is pure and fully
/// tested. This class only gathers the inputs, dispatches off the gateway thread, and makes
/// sure nothing thrown here can reach it.
/// </para>
/// </remarks>
public sealed class EntrySoundCoordinator(
    SoundboardEvents events,
    PlaybackService playback,
    VoiceStateTracker voiceStates,
    GuildUserDirectory users,
    IServiceScopeFactory scopeFactory,
    ILogger<EntrySoundCoordinator> logger,
    TimeProvider? timeProvider = null) : IHostedService, IDisposable
{
    private readonly EntrySoundPolicy _policy = new(timeProvider);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Its own cooldown, deliberately not <c>PlaybackService</c>'s. That one is stamped by
    /// button presses and driven by a config knob; sharing it would mean pressing a board
    /// button silences your own entry sound, and the administrator's live setting would
    /// fight <c>SoundboardOptions.PerUserCooldownSeconds</c>.
    /// </summary>
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastEntryPlayed = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        events.VoiceMemberArrived += OnVoiceMemberArrived;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        events.VoiceMemberArrived -= OnVoiceMemberArrived;
        return Task.CompletedTask;
    }

    public void Dispose() => events.VoiceMemberArrived -= OnVoiceMemberArrived;

    /// <summary>
    /// Raised synchronously on NetCord's callback thread, so this hands off and returns.
    /// Matches how the playback pump already schedules its own idle leave.
    /// </summary>
    private void OnVoiceMemberArrived(VoiceArrival arrival) =>
        _ = Task.Run(() => HandleArrivalAsync(arrival), CancellationToken.None);

    private async Task HandleArrivalAsync(VoiceArrival arrival)
    {
        try
        {
            // The overwhelmingly common case — the bot is not in that channel, or not
            // connected at all — costs one field read and no database work.
            if (playback.ConnectedChannelId != arrival.ChannelId)
            {
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var entrySounds = scope.ServiceProvider.GetRequiredService<EntrySoundLibrary>();

            var settings = await entrySounds.GetSettingsAsync();
            var assignment = await entrySounds.FindAsync(arrival.UserId);
            var blocked = await entrySounds.FindBlockAsync(arrival.UserId) is not null;

            var request = new EntrySoundRequest(
                UserId: arrival.UserId,
                ChannelId: arrival.ChannelId,
                BotChannelId: playback.ConnectedChannelId,

                // Excluding the arriving user makes this answer the same whether or not
                // NetCord has already applied their update to the cache.
                OtherHumansInChannel: voiceStates.CountHumansIn(arrival.ChannelId, arrival.UserId),

                AssignedSoundId: assignment?.SoundId,
                IsSelfMuted: assignment?.IsMuted ?? false,
                IsBlocked: blocked,
                LastEntryPlayedAt: _lastEntryPlayed.TryGetValue(arrival.UserId, out var last)
                    ? last
                    : null);

            var decision = _policy.Decide(settings, request);

            if (!decision.ShouldPlay)
            {
                return;
            }

            // Stamped before playing, so two arrivals racing cannot both get through.
            _lastEntryPlayed[arrival.UserId] = _time.GetUtcNow();

            var userName = users.Resolve(arrival.UserId).DisplayName;

            var result = await playback.PlayEntrySoundAsync(
                arrival.UserId, userName, arrival.ChannelId, decision.SoundId, settings.Gain);

            if (!result.IsSuccess)
            {
                logger.LogInformation(
                    "Entry sound for {UserId} did not play: {Reason}", arrival.UserId, result.Error);
            }
        }
        catch (Exception ex)
        {
            // Nothing above may escape: this runs on a fire-and-forget task, so an unhandled
            // exception would surface only as an unobserved task fault.
            logger.LogWarning(ex, "Could not play an entry sound for {UserId}", arrival.UserId);
        }
    }
}
