using Microsoft.Extensions.Options;
using SemperSounds.Core.Configuration;
using SemperSounds.Core.Sounds;

namespace SemperSounds.Web.Services;

/// <param name="Raw">Canonical Discord form, ready to store on a sound.</param>
public readonly record struct GuildEmoji(ulong Id, string Name, bool IsAnimated, string Raw)
{
    public string ImageUrl => $"https://cdn.discordapp.com/emojis/{Id}.{(IsAnimated ? "gif" : "png")}";
}

/// <summary>
/// Lists the server's custom emoji for the picker.
/// </summary>
/// <remarks>
/// Reads NetCord's guild cache, which is already kept current from the gateway, so this
/// costs no API calls and needs no cache of its own.
/// </remarks>
public sealed class GuildEmojiProvider(DiscordBotService bot, IOptions<DiscordOptions> options)
{
    private readonly ulong _guildId = options.Value.GuildId;

    public IReadOnlyList<GuildEmoji> GetAll()
    {
        if (!bot.Client.Cache.Guilds.TryGetValue(_guildId, out var guild))
        {
            return [];
        }

        return [.. guild.Emojis.Values
            .Where(emoji => emoji.Name is not null)
            .Select(emoji => new GuildEmoji(
                emoji.Id,
                emoji.Name!,
                emoji.Animated,
                $"<{(emoji.Animated ? "a" : string.Empty)}:{emoji.Name}:{emoji.Id}>"))
            .OrderBy(emoji => emoji.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Resolves a stored value for display. Returns null when it is a standard emoji, or
    /// when a custom one no longer exists on the server — callers then fall back to
    /// <see cref="SoundEmoji.Display"/>, which still reads as :name:.
    /// </summary>
    public string? GetImageUrl(string storedEmoji) =>
        SoundEmoji.TryParse(storedEmoji, out var emoji) && emoji.IsCustom
            ? emoji.ImageUrl
            : null;
}
