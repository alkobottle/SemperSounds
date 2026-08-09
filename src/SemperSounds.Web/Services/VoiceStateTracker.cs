using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Gateway;
using SemperSounds.Core.Configuration;

namespace SemperSounds.Web.Services;

/// <param name="AvatarUrl">Null when the user has no custom avatar.</param>
public readonly record struct VoiceMember(ulong UserId, string DisplayName, string? AvatarUrl);

/// <summary>
/// Answers "who is in which voice channel" for the configured guild.
/// </summary>
/// <remarks>
/// This deliberately reads NetCord's guild cache rather than maintaining its own
/// dictionary. NetCord already applies every VOICE_STATE_UPDATE to that cache, and a
/// second copy would only add a way for the two to disagree.
/// </remarks>
public sealed class VoiceStateTracker(
    DiscordBotService bot,
    GuildUserDirectory users,
    IOptions<DiscordOptions> options)
{
    private readonly ulong _guildId = options.Value.GuildId;

    private Guild? Guild =>
        bot.Client.Cache.Guilds.TryGetValue(_guildId, out var guild) ? guild : null;

    /// <summary>The voice channel the user is currently in, or null if they are in none.</summary>
    public ulong? GetChannelOf(ulong userId) =>
        Guild?.VoiceStates.TryGetValue(userId, out var state) == true ? state.ChannelId : null;

    public bool IsInChannel(ulong userId, ulong channelId) => GetChannelOf(userId) == channelId;

    /// <summary>Everyone currently sitting in the given channel, bots included.</summary>
    public IReadOnlyList<VoiceMember> GetMembersIn(ulong channelId)
    {
        var guild = Guild;
        if (guild is null)
        {
            return [];
        }

        return [.. guild.VoiceStates.Values
            .Where(state => state.ChannelId == channelId)
            .Select(state => users.Resolve(state.UserId))
            .Select(user => new VoiceMember(user.UserId, user.DisplayName, user.AvatarUrl))
            .OrderBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Human members only — used to decide when the bot should leave an empty channel.
    /// </summary>
    /// <returns>
    /// Null when the guild is not in the cache, which is <em>not</em> the same as an empty
    /// channel: the cache is briefly absent after a gateway session is invalidated and
    /// before GUILD_CREATE repopulates it, and reporting 0 there would eject the bot from
    /// a channel people are still sitting in.
    /// </returns>
    public int? CountHumansIn(ulong channelId)
    {
        var guild = Guild;
        if (guild is null)
        {
            return null;
        }

        return guild.VoiceStates.Values.Count(state =>
            state.ChannelId == channelId &&
            !(guild.Users.TryGetValue(state.UserId, out var user) && user.IsBot));
    }

    public string GetChannelName(ulong channelId) =>
        Guild?.Channels.TryGetValue(channelId, out var channel) == true
            ? channel.Name ?? "voice"
            : "voice";

}
