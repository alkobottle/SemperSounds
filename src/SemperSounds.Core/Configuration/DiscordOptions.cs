using System.ComponentModel.DataAnnotations;

namespace SemperSounds.Core.Configuration;

/// <summary>
/// Discord application credentials. Bound from the "Discord" configuration section,
/// which in the container comes from Discord__BotToken, Discord__ClientId, etc.
/// </summary>
public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    /// <summary>Bot token from the Discord developer portal (Bot -> Token).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Discord__BotToken is required.")]
    public string BotToken { get; set; } = string.Empty;

    /// <summary>OAuth2 client ID (the application ID).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Discord__ClientId is required.")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client secret.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Discord__ClientSecret is required.")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The one guild this instance serves. Users must be a member of it to sign in,
    /// and the bot only tracks voice state here.
    /// </summary>
    [Range(1UL, ulong.MaxValue, ErrorMessage = "Discord__GuildId is required and must be a Discord snowflake.")]
    public ulong GuildId { get; set; }
}
