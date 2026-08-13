using Microsoft.Extensions.Options;
using NetCord;
using SemperSounds.Core.Configuration;
using SemperSounds.Core.EntrySounds;

namespace SemperSounds.Web.Services;

/// <summary>
/// Reads guild administrator rights from NetCord's live cache.
/// </summary>
/// <remarks>
/// <para>
/// Live rather than from claims: the auth cookie lasts thirty days with sliding expiration,
/// so a promotion would need a sign-out to take effect and a demotion would leave the rights
/// standing for a month.
/// </para>
/// <para>
/// Everything here can return null, and null is not "no". <c>Guild.Users</c> is filled
/// asynchronously by <c>RequestGuildUsersAsync</c> and the chunks that follow, so for a few
/// seconds after every restart nobody is in it — and it stays empty entirely if the
/// privileged GuildUsers intent is ever revoked. Reporting false there would say
/// "you are not an administrator" when the truth is "not known yet".
/// </para>
/// </remarks>
public sealed class GuildPermissionProvider(
    DiscordBotService bot,
    IOptions<DiscordOptions> options) : IGuildPermissions
{
    private readonly ulong _guildId = options.Value.GuildId;

    public bool? IsAdministrator(ulong userId)
    {
        if (userId == 0 || !bot.IsReady)
        {
            return null;
        }

        if (!bot.Client.Cache.Guilds.TryGetValue(_guildId, out var guild))
        {
            return null;
        }

        // The owner holds every permission regardless of roles, and this needs no member
        // cache at all — so ownership survives the warm-up window above.
        if (guild.OwnerId == userId)
        {
            return true;
        }

        if (!guild.Users.TryGetValue(userId, out var user))
        {
            return null;
        }

        // NetCord ships GetPermissions only over RestGuild, not the gateway Guild, so the
        // roles are OR'd by hand. EveryoneRole is nullable on this type.
        var permissions = guild.EveryoneRole?.Permissions ?? default;

        foreach (var roleId in user.RoleIds)
        {
            if (guild.Roles.TryGetValue(roleId, out var role))
            {
                permissions |= role.Permissions;
            }
        }

        return permissions.HasFlag(Permissions.Administrator);
    }
}
