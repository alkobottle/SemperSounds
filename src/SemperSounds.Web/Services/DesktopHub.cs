using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SemperSounds.Contracts;
using SemperSounds.Core.Sounds;

namespace SemperSounds.Web.Services;

/// <summary>
/// What the Windows companion app talks to.
/// </summary>
/// <remarks>
/// <para>
/// Every method here is deliberately thin. <see cref="Play"/> is one call into
/// <see cref="PlaybackService.PlayAsync"/> and a mapping of its answer — no authorization, no
/// cooldown, no logging of its own. That is what makes a hotkey press indistinguishable from
/// a tile click: the same rule refuses it, the same cooldown paces it, and the same
/// <c>Played</c> row reaches the activity log, so <c>/stats</c> keeps counting presses without
/// knowing this path exists.
/// </para>
/// <para>
/// Auto-summoning is not modelled here. The client calls <see cref="Join"/> and retries, which
/// is exactly what a person does on the board, and so grants the app nothing the board does
/// not already grant everyone.
/// </para>
/// <para>
/// Unlike <see cref="PlaybackService"/> this may take the scoped <see cref="SoundLibrary"/>
/// directly: a hub instance is created per invocation, inside its own DI scope, so there is no
/// captured context to go stale.
/// </para>
/// </remarks>
[Authorize(AuthenticationSchemes = DeviceTokenDefaults.Scheme)]
public sealed class DesktopHub(
    PlaybackService playback,
    VoiceStateTracker voiceStates,
    DiscordBotService bot,
    SoundLibrary library,
    DesktopBroadcaster broadcaster) : Hub<ISoundboardClient>
{
    private ulong UserId => Context.User!.GetDiscordUserId();

    private string UserName => Context.User!.GetDisplayName();

    public override async Task OnConnectedAsync()
    {
        broadcaster.Register(Context.ConnectionId, UserId, Context.User!.GetDeviceTokenId(), Context.Abort);

        // Pushed rather than waited for: the client has a tray icon to colour before it gets
        // round to asking anything.
        await Clients.Caller.StateChanged(CurrentState());
        await Clients.Caller.NowPlayingChanged(playback.NowPlaying);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        broadcaster.Unregister(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task<BotState> GetState() => Task.FromResult(CurrentState());

    public Task<IReadOnlyList<NowPlaying>> GetNowPlaying() => Task.FromResult(playback.NowPlaying);

    public async Task<IReadOnlyList<SoundSummary>> GetSounds()
    {
        var sounds = await library.GetAllAsync(Context.ConnectionAborted);

        return [.. sounds.Select(s =>
        {
            // Flattened here because Contracts cannot see the parser, and because a client
            // rendering Discord's raw <:name:id> form would show the markup, not the emoji.
            var emoji = SoundEmoji.Parse(s.Emoji);
            return new SoundSummary(s.Id, s.Name, emoji.Display, emoji.ImageUrl, [.. s.TagList], s.DurationMs);
        })];
    }

    public async Task<PlayResult> Play(Guid soundId) =>
        Map(await playback.PlayAsync(soundId, UserId, UserName, Context.ConnectionAborted));

    public async Task<PlayResult> Join() =>
        Map(await playback.JoinAsync(UserId, UserName, Context.ConnectionAborted));

    public Task Leave() => playback.LeaveAsync(UserId, UserName);

    public void StopAll() => playback.StopAll();

    private BotState CurrentState() => BotStateFactory.For(
        bot.IsReady, playback.ConnectedChannelId, playback.ConnectedChannelName, voiceStates.GetChannelOf(UserId));

    /// <summary>
    /// Turns the service's answer into the wire one.
    /// </summary>
    /// <remarks>
    /// A straight copy, because <see cref="PlaybackResult"/> now carries the reason itself.
    /// The two types stay separate rather than sharing one: the service's result is a shape
    /// the web board also uses, and publishing it as a wire contract would freeze it against
    /// changes that have nothing to do with the desktop client.
    /// </remarks>
    private static PlayResult Map(PlaybackResult result) =>
        result.IsSuccess ? PlayResult.Ok : PlayResult.Fail(result.Failure, result.Error);
}
