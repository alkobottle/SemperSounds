using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using SemperSounds.Core.Audio;
using SemperSounds.Core.Configuration;
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
    private DateTimeOffset _emptySince = DateTimeOffset.MaxValue;
    private bool _speaking;
    private DateTimeOffset _silentSince = DateTimeOffset.MaxValue;
    private IReadOnlySet<Guid> _playing = new HashSet<Guid>();

    /// <summary>
    /// How long to stay in the speaking state after the last audio ends. Rapid-fire
    /// sounds would otherwise toggle the flag repeatedly, and the speaking payload is
    /// rate-limited on the voice websocket.
    /// </summary>
    private static readonly TimeSpan SpeakingLinger = TimeSpan.FromMilliseconds(400);

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
    public async Task<PlaybackResult> JoinAsync(ulong userId, CancellationToken cancellationToken = default)
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

            await DisconnectCoreAsync();

            var voiceClient = await bot.Client.JoinVoiceChannelAsync(
                _discord.GuildId, channelId, cancellationToken: cancellationToken);

            await voiceClient.StartAsync(cancellationToken);

            // Deliberately NOT entering the speaking state here. Discord lights the green
            // ring from this flag, and holding it for the whole session makes an idle bot
            // look like it is permanently transmitting. The pump raises and lowers it
            // around actual audio instead.
            _speaking = false;

            // CreateVoiceStream paces packets itself, so the pump can write frames in a
            // tight loop and let the stream throttle to real time.
            var voiceStream = voiceClient.CreateVoiceStream();
            _audioStream = new OpusEncodeStream(
                voiceStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);

            _voiceClient = voiceClient;
            ConnectedChannelId = channelId;
            _emptySince = DateTimeOffset.MaxValue;

            _pumpCancellation = new CancellationTokenSource();
            _pumpTask = Task.Run(() => PumpAsync(_pumpCancellation.Token), CancellationToken.None);

            logger.LogInformation("Joined voice channel {ChannelId}", channelId);
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

    public async Task LeaveAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
        }
        finally
        {
            _connectionGate.Release();
        }

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

        await library.LogPlayAsync(sound, userId, userName, channelId, cancellationToken);

        events.RaiseSoundPlayed(new SoundPlayedNotification(
            sound.Id, sound.Name, userId, userName, DateTimeOffset.UtcNow));

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
    /// Writes one mixed frame at a time for as long as the bot is connected, including
    /// silence. Keeping the cadence unbroken is simpler and steadier than starting and
    /// stopping the stream around each sound, and the bandwidth is negligible.
    /// </summary>
    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var frame = new byte[AudioFormat.BytesPerFrame];
        var stream = _audioStream!;
        var idleCheck = DateTimeOffset.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var hasAudio = _mixer.MixNextFrame(frame);
                await stream.WriteAsync(frame, cancellationToken);

                await UpdateSpeakingStateAsync(hasAudio, cancellationToken);

                // Cheap enough to check once a second rather than every frame.
                if (DateTimeOffset.UtcNow - idleCheck > TimeSpan.FromSeconds(1))
                {
                    idleCheck = DateTimeOffset.UtcNow;
                    if (ShouldLeaveIdleChannel())
                    {
                        _ = Task.Run(LeaveAsync, CancellationToken.None);
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
            _ = Task.Run(LeaveAsync, CancellationToken.None);
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

        if (hasAudio)
        {
            _silentSince = DateTimeOffset.MaxValue;

            if (!_speaking)
            {
                await SetSpeakingAsync(true, cancellationToken);
            }

            return;
        }

        if (!_speaking)
        {
            return;
        }

        if (_silentSince == DateTimeOffset.MaxValue)
        {
            _silentSince = DateTimeOffset.UtcNow;
        }
        else if (DateTimeOffset.UtcNow - _silentSince >= SpeakingLinger)
        {
            await SetSpeakingAsync(false, cancellationToken);
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

            _speaking = speaking;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed speaking update is cosmetic; audio still flows. Do not kill the pump.
            logger.LogDebug(ex, "Could not update the speaking state");
        }
    }

    private bool ShouldLeaveIdleChannel()
    {
        if (_options.IdleLeaveMinutes <= 0 || ConnectedChannelId is not { } channelId)
        {
            return false;
        }

        if (voiceStates.CountHumansIn(channelId) > 0)
        {
            _emptySince = DateTimeOffset.MaxValue;
            return false;
        }

        if (_emptySince == DateTimeOffset.MaxValue)
        {
            _emptySince = DateTimeOffset.UtcNow;
            return false;
        }

        return DateTimeOffset.UtcNow - _emptySince >= TimeSpan.FromMinutes(_options.IdleLeaveMinutes);
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
        _speaking = false;
        _silentSince = DateTimeOffset.MaxValue;

        if (_audioStream is not null)
        {
            try
            {
                await _audioStream.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error disposing the audio stream");
            }

            _audioStream = null;
        }

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
        _emptySince = DateTimeOffset.MaxValue;
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
        }
        finally
        {
            _connectionGate.Release();
        }

        _connectionGate.Dispose();
    }
}
