
namespace SemperSounds.Web.Services;

/// <summary>
/// Decides whether a desktop client's callback URL is genuinely a loopback address.
/// </summary>
/// <remarks>
/// <para>
/// This is the security boundary of device pairing. <c>/device/authorize</c> sends the
/// signed-in user's browser to this URL carrying a one-time code, and that code can be
/// exchanged for a credential that acts as them. Anything that is not literally their own
/// machine is therefore an open redirect with a credential attached.
/// </para>
/// <para>
/// It is an allow-list of exact shapes rather than a list of things to reject, because the
/// ways a URL can look local without being local are open-ended — userinfo before the host,
/// a subdomain that starts with the right characters, an integer or hex spelling of the same
/// address. A rule that enumerated those would be one novelty behind forever.
/// </para>
/// </remarks>
public static class LoopbackRedirect
{
    /// <summary>
    /// Ports below this are never handed out by the OS when a client asks for a free one, so a
    /// lower one means the URL was written by hand rather than produced by the pairing flow.
    /// </summary>
    private const int LowestAllowedPort = 1024;

    public static bool IsAllowed(string? redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri) ||
            !Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // Plain http only: the client's HttpListener has no certificate, so an https callback
        // could not be its own and must be somebody else's.
        if (uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        // "http://127.0.0.1@evil.test/" has a host of evil.test and reads as local to a human.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (uri.Port < LowestAllowedPort)
        {
            return false;
        }

        // Matched against the original text, deliberately, and not against the parsed host.
        // Uri canonicalises "127.1", "2130706433" and "0x7f.0.0.1" into 127.0.0.1 while
        // parsing, so by the time there is a Host property to read, every spelling anyone
        // invents has already become the canonical one and there is nothing left to reject.
        //
        // All three do point at this machine, so none of them is an escape on its own. The
        // reason to insist on the two forms the pairing flow actually emits is that it costs
        // a real client nothing and leaves no room for our parser and the browser's to reach
        // different conclusions about the same string — which is the shape the genuinely
        // dangerous cases above take.
        return redirectUri.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)
            || redirectUri.StartsWith("http://[::1]:", StringComparison.OrdinalIgnoreCase);
    }
}
