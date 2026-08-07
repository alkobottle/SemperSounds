using Microsoft.Extensions.Options;
using NetCord;
using SemperSounds.Core.Configuration;

namespace SemperSounds.Web.Services;

/// <summary>
/// Checks the bot token actually parses before anything tries to use it.
/// Without this, a typo'd or placeholder token surfaces as an ArgumentException from
/// deep inside DI construction, buried in a stack trace that says nothing useful.
/// </summary>
public sealed class DiscordOptionsValidator : IValidateOptions<DiscordOptions>
{
    public ValidateOptionsResult Validate(string? name, DiscordOptions options)
    {
        try
        {
            _ = new BotToken(options.BotToken);
        }
        catch (Exception)
        {
            return ValidateOptionsResult.Fail(
                "Discord__BotToken is not a valid bot token. Copy it from the Discord developer " +
                "portal under Bot -> Reset Token. Note this is NOT the client secret, and not the " +
                "application ID.");
        }

        if (options.GuildId < 1000)
        {
            return ValidateOptionsResult.Fail(
                "Discord__GuildId does not look like a Discord server ID. Enable Developer Mode in " +
                "Discord, then right-click the server and choose Copy Server ID.");
        }

        return ValidateOptionsResult.Success;
    }
}
