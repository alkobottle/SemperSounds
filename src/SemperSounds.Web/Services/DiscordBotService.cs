using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Gateway;
using SemperSounds.Core.Configuration;

namespace SemperSounds.Web.Services;

/// <summary>
/// Owns the Discord gateway connection for the lifetime of the app.
/// Runs in the same process as the web UI, so pages reach the bot through DI
/// rather than any inter-process channel.
/// </summary>
public sealed class DiscordBotService : IHostedService, IDisposable
{
    private readonly DiscordOptions _options;
    private readonly SoundboardEvents _events;
    private readonly ILogger<DiscordBotService> _logger;

    public DiscordBotService(
        IOptions<DiscordOptions> options,
        SoundboardEvents events,
        ILogger<DiscordBotService> logger)
    {
        _options = options.Value;
        _events = events;
        _logger = logger;

        Client = new GatewayClient(
            new BotToken(_options.BotToken),
            new GatewayClientConfiguration
            {
                // GuildUsers is privileged and must also be enabled in the developer portal
                // (Bot -> Server Members Intent). Without it Discord sends only the bot's
                // own member object in GUILD_CREATE, leaving Guild.Users effectively empty
                // — which silently broke avatar lookups and the idle auto-leave.
                Intents = GatewayIntents.Guilds
                    | GatewayIntents.GuildVoiceStates
                    | GatewayIntents.GuildUsers,
            });
    }

    public GatewayClient Client { get; }

    private readonly GatewayReadiness _readiness = new();

    /// <summary>True once the gateway has identified or resumed and the cache is usable.</summary>
    public bool IsReady => _readiness.IsReady;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Client.Ready += OnReadyAsync;
        Client.Resume += OnResumeAsync;
        Client.VoiceStateUpdate += OnVoiceStateUpdateAsync;
        Client.GuildCreate += OnGuildCreateAsync;
        Client.GuildUserChunk += OnGuildUserChunkAsync;
        Client.Disconnect += OnDisconnectAsync;

        // Deliberately not awaited to completion: StartAsync connects and then runs the
        // gateway loop, and blocking here would stall application startup.
        await Client.StartAsync(cancellationToken: cancellationToken);
        _logger.LogInformation("Discord gateway connecting for guild {GuildId}", _options.GuildId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _readiness.MarkDisconnected();
        try
        {
            await Client.CloseAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing the Discord gateway connection");
        }
    }

    private ValueTask OnReadyAsync(ReadyEventArgs args)
    {
        _readiness.MarkReady();
        _logger.LogInformation("Discord gateway ready as {User}", args.User.Username);
        _events.RaiseConnectionChanged();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// A dropped socket is usually restored by resuming the session, which Discord answers
    /// with RESUMED rather than READY. Without this the bot stays marked unusable for the
    /// rest of the process's life — refusing every join and play — while the gateway
    /// carries on heartbeating perfectly happily.
    /// </summary>
    private ValueTask OnResumeAsync()
    {
        _readiness.MarkResumed();
        _logger.LogInformation("Discord gateway resumed its session");
        _events.RaiseConnectionChanged();
        return ValueTask.CompletedTask;
    }

    private ValueTask OnDisconnectAsync(DisconnectEventArgs args)
    {
        _readiness.MarkDisconnected();

        // Logged because this is the entry to the only state in which the UI refuses to
        // work; diagnosing it from the outside otherwise means reading socket counters.
        _logger.LogWarning(
            "Discord gateway disconnected (reconnecting: {Reconnect})", args.Reconnect);

        _events.RaiseConnectionChanged();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// GUILD_CREATE carries the full voice state list, which is how the UI knows who is
    /// already sitting in a channel without waiting for someone to move.
    /// </summary>
    private async ValueTask OnGuildCreateAsync(GuildCreateEventArgs args)
    {
        if (args.Guild?.Id != _options.GuildId)
        {
            return;
        }

        _logger.LogInformation(
            "Guild {Name} cached with {VoiceStates} voice states and {Users} users",
            args.Guild.Name, args.Guild.VoiceStates.Count, args.Guild.Users.Count);

        _events.RaiseVoiceStateChanged();

        // Above Discord's large_threshold (50 by default) GUILD_CREATE omits members, so a
        // 51-member guild arrives with an all-but-empty user list. Asking for them
        // explicitly is size-independent, unlike raising the threshold, which would only
        // postpone the same silent failure to 250 members.
        try
        {
            await Client.RequestGuildUsersAsync(
                new GuildUsersRequestProperties(_options.GuildId) { Query = string.Empty, Limit = 0 });
        }
        catch (Exception ex)
        {
            // Not fatal: avatars and names fall back to initials, and playback is unaffected.
            _logger.LogWarning(ex, "Could not request the guild member list");
        }
    }

    /// <summary>
    /// Members arrive in chunks after <see cref="OnGuildCreateAsync"/> asks for them.
    /// NetCord folds each chunk into the guild cache; this only reports progress and
    /// nudges the UI so avatars appear without a reload.
    /// </summary>
    private ValueTask OnGuildUserChunkAsync(GuildUserChunkEventArgs args)
    {
        if (args.GuildId == _options.GuildId)
        {
            _logger.LogInformation(
                "Received member chunk {Index} of {Count} ({Users} users)",
                args.ChunkIndex + 1, args.ChunkCount, args.Users.Count);

            _events.RaiseVoiceStateChanged();
        }

        return ValueTask.CompletedTask;
    }

    private ValueTask OnVoiceStateUpdateAsync(VoiceState voiceState)
    {
        if (voiceState.GuildId == _options.GuildId)
        {
            _events.RaiseVoiceStateChanged();
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        Client.Ready -= OnReadyAsync;
        Client.Resume -= OnResumeAsync;
        Client.VoiceStateUpdate -= OnVoiceStateUpdateAsync;
        Client.GuildCreate -= OnGuildCreateAsync;
        Client.GuildUserChunk -= OnGuildUserChunkAsync;
        Client.Disconnect -= OnDisconnectAsync;
        Client.Dispose();
    }
}
