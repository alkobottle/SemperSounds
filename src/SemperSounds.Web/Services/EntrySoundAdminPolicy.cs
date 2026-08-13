using Microsoft.AspNetCore.Authorization;
using SemperSounds.Core.EntrySounds;

namespace SemperSounds.Web.Services;

public static class EntrySoundPolicies
{
    public const string Administrator = "entry-sounds:admin";
}

public sealed class EntrySoundAdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Grants the admin policy to guild administrators, evaluated per request.
/// </summary>
/// <remarks>
/// Fails closed when <see cref="IGuildPermissions.IsAdministrator"/> answers null, which
/// happens while the member cache is still filling after a restart. The admin page tells
/// the two apart for the reader's sake; this handler deliberately does not.
/// </remarks>
public sealed class EntrySoundAdminHandler(IGuildPermissions permissions)
    : AuthorizationHandler<EntrySoundAdminRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, EntrySoundAdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            permissions.IsAdministrator(context.User.GetDiscordUserId()) is true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
