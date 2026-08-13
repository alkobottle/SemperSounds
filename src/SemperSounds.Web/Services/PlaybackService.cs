using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using SemperSounds.Core.Audio;
using SemperSounds.Core.Configuration;
using SemperSounds.Core.Data;
using SemperSounds.Core.EntrySounds;
using SemperSounds.Core.Sounds;

namespace SemperSounds.Web.Services;

/// <param name="Error">Empty when the operation succeeded.</param>
public readonly record struct PlaybackResult(bool IsSuccess, string Error)
{
    public static PlaybackResult Ok => new(true, string.Empty);
    public static PlaybackResult Fail(string error) => new(false, error);
}

/// <summary>
/// Owns the bot's voice connection and the pump that feeds mixed audio into it.
/// </summary>
public sealed class PlaybackService(
    DiscordBotService bot,
    VoiceStateTracker voiceStates,
    SoundboardEvents events,
    IServiceScopeFactory scopeFactory,
    IOptions<DiscordOptions> discordOptions,
    IOptions<SoundboardOptions> soundboardOptions,
    ILogger<PlaybackService> logger) : IAsyncDisposable
{
    private readonly DiscordOptions _discord = discordOptions.Value;
    private readonly SoundboardOptions _options = soundboardOptions.Value;
    private readonly PcmMixer _mixer = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _lastPlayed = new();

    private VoiceClient? _voiceClient;
    private Stream? _audioStream;
    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;
    private IReadOnlySet<Guid> _playing = new HashSet<Guid>();

    /// <summary>Drops the bot out once nobody is left to hear it.</summary>
    private readonly IdleTimer _idleTimer =
        new(TimeSpan.FromSeconds(soundboardOptions.Value.IdleLeaveSeconds));

    /// <summary>
    /// How long to stay in the speaking state after the last audio ends. Rapid-fire
    /// sounds would otherwise toggle the flag repeatedly, and the speaking payload is
    /// rate-limited on the voice websocket.
    /// </summary>
    private readonly SpeakingGate _speakingGate = new(TimeSpan.FromMilliseconds(400));

    /// <summary>Sound IDs currently sounding, so the UI can show them as playing.</summary>
    public IReadOnlySet<Guid> PlayingSoundIds => _playing;

    public bool IsPlaying(Guid soundId) => _playing.Contains(soundId);

    /// <summary>The channel the bot is currently connected to, if any.</summary>
    public ulong? ConnectedChannelId { get; private set; }

    public bool IsConnected => ConnectedChannelId is not null;

    public string ConnectedChannelName =>
        ConnectedChannelId is { } id ? voiceStates.GetChannelName(id) : string.Empty;

    /// <summary>
    /// Connects the bot to whichever voice channel the requesting user is sitting in.
    /// </summary>
    public async Task<PlaybackResult> JoinAsync(
        ulong userId, string userName = "", CancellationToken cancellationToken = default)
    {
        if (!bot.IsReady)
        {
            return PlaybackResult.Fail("The bot is not connected to Discord yet. Try again in a moment.");
        }

        if (voiceStates.GetChannelOf(userId) is not { } channelId)
        {
            return PlaybackResult.Fail("Join a voice channel first, then press Join again.");
        }

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (ConnectedChannelId == channelId)
            {
                return PlaybackResult.Ok;
            }

            // Moving between channels is a departure followed by an arrival. Without this
            // the log shows the bot summoned to two channels and never leaving the first.
            var previousChannelId = ConnectedChannelId;
            var previousChannelName = ConnectedChannelName;

            await DisconnectCoreAsync();

            if (previousChannelId is { } leftId)
            {
                await LogActivityAsync(
                    log => log.LogLeaveAsync(userId, userName, leftId, previousChannelName));
            }

            var voiceClient = await bot.Client.JoinVoiceChannelAsync(
                _discord.GuildId, channelId, cancellationToken: cancellationToken);

            await voiceClient.StartAsync(cancellationToken);

            // Required, and not merely cosmetic: this is what readies the connection for
            // sending. Skipping it makes the first SendVoiceAsync throw "Connection not
            // started". The pump lowers it again shortly afterwards, so the green ring
            // does not stay lit for the whole session.
            await voiceClient.EnterSpeakingStateAsync(
                new SpeakingProperties(SpeakingFlags.Microphone), cancellationToken: cancellationToken);

            // Start from "speaking", matching the call just made, and let the pump lower it
            // once the linger expires. The audio stream itself is opened lazily per burst,
            // so an idle bot sends nothing at all.
            _speakingGate.Reset(speaking: true);

            _voiceClient = voiceClient;
            ConnectedChannelId = channelId;
            _idleTimer.Reset();

            _pumpCancellation = new CancellationTokenSource();
            _pumpTask = Task.Run(() => PumpAsync(_pumpCancellation.Token), CancellationToken.None);

            logger.LogInformation("Joined voice channel {ChannelId}", channelId);
            await LogActivityAsync(
                log => log.LogJoinAsync(userId, userName, channelId, voiceStates.GetChannelName(channelId)));

            events.RaiseConnectionChanged();
            return PlaybackResult.Ok;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to join voice channel");
            await DisconnectCoreAsync();
            return PlaybackResult.Fail($"Could not join the voice channel: {ex.Message}");
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <param name="userId">Null when the bot is leaving on its own because nobody remained.</param>
    public async Task LeaveAsync(ulong? userId = null, string? userName = null)
    {
        ulong? channelId;
        string channelName;

        await _connectionGate.WaitAsync();
        try
        {
            // Read inside the gate. Captured outside it, a manual Leave racing the idle
            // timer would see the same channel from both calls and log two departures for
            // one disconnect.
            channelId = ConnectedChannelId;
            channelName = ConnectedChannelName;

            await DisconnectCoreAsync();
        }
        finally
        {
            _connectionGate.Release();
        }

        // Only the caller that actually found a connection reports it.
        if (channelId is not { } id)
        {
            return;
        }

        await LogActivityAsync(log => log.LogLeaveAsync(userId, userName, id, channelName));
        events.RaiseConnectionChanged();
    }

    /// <summary>
    /// Plays a sound into the connected channel. Authorization lives here rather than in
    /// the UI: disabled buttons are a hint, this is the rule.
    /// </summary>
    public async Task<PlaybackResult> PlayAsync(
        Guid soundId, ulong userId, string userName, CancellationToken cancellationToken = default)
    {
        if (ConnectedChannelId is not { } channelId)
        {
            return PlaybackResult.Fail("The bot is not in a voice channel. Press Join first.");
        }

        if (!voiceStates.IsInChannel(userId, channelId))
        {
            return PlaybackResult.Fail("You have to be in the same voice channel as the bot to play sounds.");
        }

        if (IsOnCooldown(userId, out var remaining))
        {
            return PlaybackResult.Fail($"Slow down — {remaining.TotalSeconds:0.#}s to go.");
        }

        using var scope = scopeFactory.CreateScope();
        var library = scope.ServiceProvider.GetRequiredService<SoundLibrary>();

        var sound = await library.FindAsync(soundId, cancellationToken);
        if (sound is null)
        {
            return PlaybackResult.Fail("That sound no longer exists.");
        }

        var pcm = await library.ReadPcmAsync(sound, cancellationToken);
        if (pcm is null)
        {
            logger.LogWarning("Audio file missing for sound {SoundId} ({Name})", sound.Id, sound.Name);
            return PlaybackResult.Fail("That sound's audio file is missing.");
        }

        _mixer.Add(pcm, sound.Id);
        _lastPlayed[userId] = DateTimeOffset.UtcNow;

        // Guarded like the other log writes: the clip is already mixing by this point, so a
        // failed write must not surface as an exception out of the caller's click handler
        // and must not stop the play being announced to other browsers.
        var channelName = ConnectedChannelName;
        await LogActivityAsync(log => log.LogPlayAsync(sound, userId, userName, channelId, channelName));

        events.RaiseSoundPlayed(new SoundPlayedNotification(
            sound.Id, sound.Name, userId, userName, DateTimeOffset.UtcNow, SoundboardActivity.Played));

        return PlaybackResult.Ok;
    }

    /// <summary>
    /// Plays somebody's entry sound into the channel they just walked into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="PlayAsync"/> there is no "you must be in the bot's channel" check.
    /// The arriving user is in it by construction, and re-reading the cache here would
    /// reintroduce exactly the ordering race the caller took care to avoid.
    /// </para>
    /// <para>
    /// What survives is the invariant that matters. Audio only ever reaches the mixer while
    /// the bot is connected to <paramref name="channelId"/> — checked before the disk read
    /// and again immediately after it — and <paramref name="soundId"/> must equal the
    /// user's own stored assignment, re-read here. So the only extra authority this grants
    /// over the board is "an existing entry sound can be re-fired in a channel the bot is
    /// already sitting in", rather than a way to play anything at anyone.
    /// </para>
    /// </remarks>
    /// <param name="gain">Server-wide entry sound volume, so they sit under conversation.</param>
    internal async Task<PlaybackResult> PlayEntrySoundAsync(
        ulong userId, string userName, ulong channelId, Guid soundId, float gain,
        CancellationToken cancellationToken = default)
    {
        if (ConnectedChannelId != channelId)
        {
            return PlaybackResult.Fail("The bot is not in that channel.");
        }

        using var scope = scopeFactory.CreateScope();
        var library = scope.ServiceProvider.GetRequiredService<SoundLibrary>();
        var entrySounds = scope.ServiceProvider.GetRequiredService<EntrySoundLibrary>();

        var assignment = await entrySounds.FindAsync(userId, cancellationToken);
        if (assignment?.SoundId != soundId)
        {
            return PlaybackResult.Fail("That is not this user's entry sound.");
        }

        var sound = await library.FindAsync(soundId, cancellationToken);
        if (sound is null)
        {
            return PlaybackResult.Fail("That sound no longer exists.");
        }

        var pcm = await library.ReadPcmAsync(sound, cancellationToken);
        if (pcm is null)
        {
            logger.LogWarning("Audio file missing for sound {SoundId} ({Name})", sound.Id, sound.Name);
            return PlaybackResult.Fail("That sound's audio file is missing.");
        }

        // Re-checked after the disk read: a Leave landing in that window would otherwise
        // spill this clip into whatever channel the bot moved to next.
        if (ConnectedChannelId != channelId)
        {
            return PlaybackResult.Fail("The bot left that channel.");
        }

        _mixer.Add(pcm, sound.Id, gain);

        var channelName = ConnectedChannelName;
        await LogActivityAsync(
            log => log.LogEntrySoundAsync(sound, userId, userName, channelId, channelName));

        // Reusing SoundPlayed rather than adding an entry-specific notification: every new
        // subscription is another handler a component can leak and pin its circuit with.
        // The kind is what keeps subscribers from counting this as a button press.
        events.RaiseSoundPlayed(new SoundPlayedNotification(
            sound.Id, sound.Name, userId, userName, DateTimeOffset.UtcNow, SoundboardActivity.EntryPlayed));

        return PlaybackResult.Ok;
    }

    /// <summary>Silences everything currently playing without leaving the channel.</summary>
    public void StopAll()
    {
        _mixer.StopAll();

        // Publish straight away rather than waiting for the next pump frame, so the tiles
        // stop looking like they are playing the instant the button is pressed.
        _playing = new HashSet<Guid>();
        events.RaisePlaybackChanged();
    }

    public int ActiveSoundCount => _mixer.ActiveCount;

    private bool IsOnCooldown(ulong userId, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (_options.PerUserCooldownSeconds <= 0 || !_lastPlayed.TryGetValue(userId, out var last))
        {
            return false;
        }

        var elapsed = DateTimeOffset.UtcNow - last;
        var cooldown = TimeSpan.FromSeconds(_options.PerUserCooldownSeconds);

        if (elapsed >= cooldown)
        {
            return false;
        }

        remaining = cooldown - elapsed;
        return true;
    }

    /// <summary>
    /// Sends audio only while there is audio to send.
    /// </summary>
    /// <remarks>
    /// This used to write silence continuously to keep the cadence steady. That is what
    /// kept Discord's green ring lit permanently: the ring follows packet flow, not just
    /// the speaking flag, so lowering the flag alone did nothing while frames kept
    /// arriving. Real clients stop transmitting when nobody is talking, and so does this.
    ///
    /// Each speaking burst also gets a fresh stream. NetCord's voice stream normalizes
    /// sending speed against a clock, and resuming a long-idle stream would let it believe
    /// it was far behind and rush frames to catch up.
    /// </remarks>
    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var frame = new byte[AudioFormat.BytesPerFrame];
        var idleCheck = DateTimeOffset.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var hasAudio = _mixer.MixNextFrame(frame);

                // Decide before writing, so the flag is raised as the first frame goes out
                // rather than one frame late.
                await UpdateSpeakingStateAsync(hasAudio, cancellationToken);

                if (_speakingGate.IsSpeaking)
                {
                    // Covers the linger too, so the tail of a clip is flushed before the
                    // stream closes rather than being clipped.
                    await EnsureAudioStreamAsync(cancellationToken);
                    await _audioStream!.WriteAsync(frame, cancellationToken);
                }
                else
                {
                    await CloseAudioStreamAsync();

                    // Nothing is being written, so the stream cannot pace us here.
                    await Task.Delay(AudioFormat.FrameMilliseconds, cancellationToken);
                }

                // Cheap enough to check once a second rather than every frame.
                if (DateTimeOffset.UtcNow - idleCheck > TimeSpan.FromSeconds(1))
                {
                    idleCheck = DateTimeOffset.UtcNow;
                    if (ShouldLeaveIdleChannel())
                    {
                        // No user: this is the bot dropping out on its own.
                        _ = Task.Run(() => LeaveAsync(), CancellationToken.None);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Voice pump stopped unexpectedly");
            _ = Task.Run(() => LeaveAsync(), CancellationToken.None);
        }
    }

    /// <summary>
    /// Raises the speaking flag while audio is sounding and lowers it once the channel has
    /// been silent for <see cref="SpeakingLinger"/>. Discord's green ring follows this flag,
    /// so leaving it raised for the whole session made an idle bot look like it was
    /// permanently transmitting. Also publishes which sounds are playing, for the UI.
    /// </summary>
    private async Task UpdateSpeakingStateAsync(bool hasAudio, CancellationToken cancellationToken)
    {
        var active = _mixer.ActiveKeys;

        if (!active.SetEquals(_playing))
        {
            _playing = active;
            events.RaisePlaybackChanged();
        }

        if (_speakingGate.Update(hasAudio) is { } speaking)
        {
            await SetSpeakingAsync(speaking, cancellationToken);
        }
    }

    private async Task SetSpeakingAsync(bool speaking, CancellationToken cancellationToken)
    {
        if (_voiceClient is not { } client)
        {
            return;
        }

        try
        {
            await client.EnterSpeakingStateAsync(
                new SpeakingProperties(speaking ? SpeakingFlags.Microphone : default),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed speaking update is cosmetic; audio still flows. Do not kill the
            // pump, but put the gate back so the next frame retries the transition.
            logger.LogDebug(ex, "Could not update the speaking state");
            _speakingGate.Reset(!speaking);
        }
    }

    /// <summary>
    /// Runs a log write in its own scope. This service is a singleton and the log is
    /// scoped, so it cannot be injected directly without capturing a disposed context.
    /// A failed write must not take the voice connection down with it.
    /// </summary>
    private async Task LogActivityAsync(Func<ActivityLog, Task> write)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await write(scope.ServiceProvider.GetRequiredService<ActivityLog>());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write to the activity log");
        }
    }

    /// <summary>Opens an audio stream for a speaking burst, if one is not already open.</summary>
    private ValueTask EnsureAudioStreamAsync(CancellationToken cancellationToken)
    {
        if (_audioStream is not null || _voiceClient is not { } client)
        {
            return ValueTask.CompletedTask;
        }

        // PcmFormat.Short matches what PcmMixer produces, so nothing converts in between.
        _audioStream = new OpusEncodeStream(
            client.CreateVoiceStream(), PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Ends a speaking burst. Flushing first lets the encoder emit its trailing silence
    /// frames, which is how Discord is told the burst is over.
    /// </summary>
    private async ValueTask CloseAudioStreamAsync()
    {
        if (_audioStream is not { } stream)
        {
            return;
        }

        _audioStream = null;

        try
        {
            await stream.FlushAsync();
            await stream.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error closing the audio stream");
        }
    }

    private bool ShouldLeaveIdleChannel()
    {
        if (ConnectedChannelId is not { } channelId)
        {
            return false;
        }

        // A null count means the guild cache is momentarily unavailable, not that the
        // channel is empty. Treating it as "someone is listening" keeps the bot in place
        // rather than dropping it out of an occupied channel while the cache repopulates.
        var humans = voiceStates.CountHumansIn(channelId);

        return _idleTimer.ShouldLeave(anyoneListening: humans is null or > 0);
    }

    /// <summary>Tears down the connection. Callers must already hold <see cref="_connectionGate"/>.</summary>
    private async Task DisconnectCoreAsync()
    {
        if (_pumpCancellation is not null)
        {
            await _pumpCancellation.CancelAsync();
        }

        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Voice pump ended with an exception during disconnect");
            }
        }

        _pumpCancellation?.Dispose();
        _pumpCancellation = null;
        _pumpTask = null;

        _mixer.StopAll();
        _playing = new HashSet<Guid>();
        _speakingGate.Reset(speaking: false);

        await CloseAudioStreamAsync();

        if (_voiceClient is not null)
        {
            try
            {
                await _voiceClient.CloseAsync();
                // Tell the gateway to actually leave the channel, otherwise the bot
                // lingers in the member list with a dead voice connection.
                await bot.Client.UpdateVoiceStateAsync(new VoiceStateProperties(_discord.GuildId, null));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error closing the voice client");
            }

            _voiceClient = null;
        }

        ConnectedChannelId = null;
        _idleTimer.Reset();
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            // Record the departure before tearing down, so a container restart does not
            // leave the log showing the bot still sitting in a channel.
            if (ConnectedChannelId is { } channelId)
            {
                var channelName = ConnectedChannelName;
                await LogActivityAsync(log => log.LogLeaveAsync(null, null, channelId, channelName));
            }

            await DisconnectCoreAsync();
        }
        finally
        {
            _connectionGate.Release();
        }

        _connectionGate.Dispose();
    }
}
