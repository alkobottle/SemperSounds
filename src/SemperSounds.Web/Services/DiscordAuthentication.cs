using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using SemperSounds.Core.Configuration;

namespace SemperSounds.Web.Services;

public static class DiscordAuthentication
{
    /// <summary>Claim holding the user's Discord avatar URL, for the app bar.</summary>
    public const string AvatarClaim = "urn:sempersounds:avatar";

    /// <summary>Failure reason used to route a rejected non-member to the right page.</summary>
    private const string NotAGuildMember = "NotAGuildMember";

    /// <summary>Reads the Discord user ID (a snowflake) out of the signed-in principal.</summary>
    public static ulong GetDiscordUserId(this ClaimsPrincipal principal) =>
        ulong.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public static string GetDisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? "unknown";

    public static string? GetAvatarUrl(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(AvatarClaim);

    public static IServiceCollection AddSemperSoundsAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var discord = configuration.GetSection(DiscordOptions.SectionName).Get<DiscordOptions>()
            ?? new DiscordOptions();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = DiscordAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "sempersounds.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax; // Lax is required for the OAuth redirect back.
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/access-denied";
            })
            .AddDiscord(options =>
            {
                options.ClientId = discord.ClientId;
                options.ClientSecret = discord.ClientSecret;

                // "guilds" lets us verify membership of the one server this instance serves.
                options.Scope.Add("guilds");
                options.SaveTokens = false;

                options.Events.OnCreatingTicket = async context =>
                {
                    var isMember = await IsGuildMemberAsync(context, discord.GuildId);

                    if (!isMember)
                    {
                        // Failing the ticket here means a non-member never gets a cookie,
                        // rather than being filtered later on every request.
                        context.Fail(NotAGuildMember);
                        return;
                    }

                    AddAvatarClaim(context);
                };

                // Without this, every unusable callback throws an unhandled exception and
                // the user gets a stack trace. That covers more than edge cases: hitting
                // /signin-discord directly, refreshing it, using the back button after a
                // completed login, letting the state expire -- and, most importantly, the
                // non-member rejection above, whose friendly page would never be reached.
                options.Events.OnRemoteFailure = context =>
                {
                    var isNonMember = context.Failure?.Message.Contains(NotAGuildMember, StringComparison.Ordinal) == true;

                    context.Response.Redirect(isNonMember ? "/access-denied" : "/login-failed");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        return services;
    }

    private static async Task<bool> IsGuildMemberAsync(OAuthCreatingTicketContext context, ulong guildId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/users/@me/guilds");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: context.HttpContext.RequestAborted);

        var wanted = guildId.ToString();

        return document.RootElement.EnumerateArray().Any(guild =>
            guild.TryGetProperty("id", out var id) && id.GetString() == wanted);
    }

    private static void AddAvatarClaim(OAuthCreatingTicketContext context)
    {
        if (!context.User.TryGetProperty("avatar", out var avatar) ||
            avatar.ValueKind != JsonValueKind.String ||
            !context.User.TryGetProperty("id", out var id))
        {
            return;
        }

        var extension = avatar.GetString()!.StartsWith("a_", StringComparison.Ordinal) ? "gif" : "png";
        context.Identity?.AddClaim(new Claim(
            AvatarClaim,
            $"https://cdn.discordapp.com/avatars/{id.GetString()}/{avatar.GetString()}.{extension}"));
    }
}
