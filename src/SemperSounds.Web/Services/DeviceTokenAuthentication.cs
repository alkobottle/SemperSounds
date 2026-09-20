using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SemperSounds.Core.Devices;

namespace SemperSounds.Web.Services;

public static class DeviceTokenDefaults
{
    public const string Scheme = "DeviceToken";

    /// <summary>Claim holding the id of the token a request arrived on, so a revoke can find it.</summary>
    public const string TokenIdClaim = "urn:sempersounds:device-token";

    /// <summary>Reads the device token id a hub connection authenticated with, if any.</summary>
    public static Guid? GetDeviceTokenId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(TokenIdClaim), out var id) ? id : null;
}

public sealed class DeviceTokenOptions : AuthenticationSchemeOptions;

/// <summary>
/// Authenticates a paired desktop client from its device token.
/// </summary>
/// <remarks>
/// <para>
/// Additive: the cookie remains the default scheme, so nothing about the Blazor circuits
/// changes. That is why an absent token returns <see cref="AuthenticateResult.NoResult"/>
/// rather than a failure — failing here would make this scheme's opinion about an ordinary
/// browser request count for something.
/// </para>
/// <para>
/// The principal it issues carries the same claim types the cookie does, so
/// <see cref="DiscordAuthentication.GetDiscordUserId"/> and
/// <see cref="DiscordAuthentication.GetDisplayName"/> work on it unchanged.
/// </para>
/// </remarks>
public sealed class DeviceTokenAuthenticationHandler(
    IOptionsMonitor<DeviceTokenOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    DeviceTokenStore store) : AuthenticationHandler<DeviceTokenOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken();
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await store.ValidateAsync(token, Context.RequestAborted);
        if (identity is not { } device)
        {
            return AuthenticateResult.Fail("The device token is unknown, revoked or expired.");
        }

        var claims = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, device.UserId.ToString()),
            new Claim(ClaimTypes.Name, device.UserName),
            new Claim(DeviceTokenDefaults.TokenIdClaim, device.TokenId.ToString()),
        ], DeviceTokenDefaults.Scheme);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(claims), DeviceTokenDefaults.Scheme));
    }

    /// <summary>
    /// A plain 401, deliberately not the cookie scheme's redirect to <c>/login</c>. A SignalR
    /// client follows the redirect, receives a sign-in page with a 200, and reports a
    /// handshake error that says nothing about the token being the problem.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private string? ReadToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..].Trim();
        }

        // The WebSocket transport cannot set request headers, so SignalR's own convention is
        // to put the token in the query string instead. Accepted only on the hub path, so no
        // other endpoint can be reached with a credential that ends up in proxy access logs.
        if (Request.Path.StartsWithSegments("/hubs") &&
            Request.Query.TryGetValue("access_token", out var fromQuery))
        {
            return fromQuery.ToString();
        }

        return null;
    }
}
