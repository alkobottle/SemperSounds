using Microsoft.Extensions.Options;
using SemperSounds.Core.Configuration;

namespace SemperSounds.Web.Services;

/// <param name="AvatarUrl">Null when the user has no avatar, or is no longer in the guild.</param>
public readonly record struct GuildUserInfo(ulong UserId, string DisplayName, string? AvatarUrl)
{
    /// <summary>Letter to show when there is no avatar image.</summary>
    public char Initial => string.IsNullOrEmpty(DisplayName) ? '?' : char.ToUpperInvariant(DisplayName[0]);
}

/// <summary>
/// Resolves a Discord user ID to a display name and avatar.
/// </summary>
/// <remarks>
/// Reads NetCord's guild cache, so avatars follow the user: change your picture in Discord
/// and it changes here, which storing a copy at upload time would not do. Costs no API
/// calls. Falls back to the name recorded at the time for anyone who has since left.
/// </remarks>
public sealed class GuildUserDirectory(DiscordBotService bot, IOptions<DiscordOptions> options)
{
    private readonly ulong _guildId = options.Value.GuildId;

    public GuildUserInfo Resolve(ulong userId, string? fallbackName = null)
    {
        if (bot.Client.Cache.Guilds.TryGetValue(_guildId, out var guild) &&
            guild.Users.TryGetValue(userId, out var user))
        {
            var name = user.Nickname ?? user.GlobalName ?? user.Username;
            return new GuildUserInfo(userId, name, user.GetAvatarUrl()?.ToString());
        }

        return new GuildUserInfo(
            userId,
            string.IsNullOrWhiteSpace(fallbackName) ? userId.ToString() : fallbackName,
            null);
    }
}
