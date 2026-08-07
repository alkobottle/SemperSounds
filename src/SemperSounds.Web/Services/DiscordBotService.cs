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
                // Both are non-privileged, so nothing needs enabling in the developer
                // portal. Voice states are what gate who may press play.
                Intents = GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates,
            });
    }

    public GatewayClient Client { get; }

    /// <summary>True once the gateway has sent READY and the guild cache is usable.</summary>
    public bool IsReady { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Client.Ready += OnReadyAsync;
        Client.VoiceStateUpdate += OnVoiceStateUpdateAsync;
        Client.GuildCreate += OnGuildCreateAsync;
        Client.Disconnect += OnDisconnectAsync;

        // Deliberately not awaited to completion: StartAsync connects and then runs the
        // gateway loop, and blocking here would stall application startup.
        await Client.StartAsync(cancellationToken: cancellationToken);
        _logger.LogInformation("Discord gateway connecting for guild {GuildId}", _options.GuildId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        IsReady = false;
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
        IsReady = true;
        _logger.LogInformation("Discord gateway ready as {User}", args.User.Username);
        _events.RaiseConnectionChanged();
        return ValueTask.CompletedTask;
    }

    private ValueTask OnDisconnectAsync(DisconnectEventArgs args)
    {
        IsReady = false;
        _events.RaiseConnectionChanged();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// GUILD_CREATE carries the full voice state list, which is how the UI knows who is
    /// already sitting in a channel without waiting for someone to move.
    /// </summary>
    private ValueTask OnGuildCreateAsync(GuildCreateEventArgs args)
    {
        if (args.Guild?.Id == _options.GuildId)
        {
            _logger.LogInformation(
                "Guild {Name} cached with {Count} voice states", args.Guild.Name, args.Guild.VoiceStates.Count);
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
        Client.VoiceStateUpdate -= OnVoiceStateUpdateAsync;
        Client.GuildCreate -= OnGuildCreateAsync;
        Client.Disconnect -= OnDisconnectAsync;
        Client.Dispose();
    }
}
