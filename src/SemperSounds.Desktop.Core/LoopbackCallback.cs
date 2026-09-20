namespace SemperSounds.Desktop.Core;

/// <summary>
/// The address the browser returns the pairing code to, in the two spellings it needs.
/// </summary>
/// <remarks>
/// <para>
/// They differ by one character and are not interchangeable. An <c>HttpListener</c> prefix must
/// end in a slash and throws <see cref="ArgumentException"/> if it does not — from
/// <c>Prefixes.Add</c>, before any request is served, so a caller guarding the listener's
/// <c>Start</c> against <c>HttpListenerException</c> never sees it coming. The
/// <c>redirect_uri</c> must not end in one, because the server stores it when the code is
/// minted and compares it verbatim when the code is redeemed.
/// </para>
/// <para>
/// The prefix is registered at the root rather than at <c>/callback/</c>. A prefix is matched
/// by path, and registering the deeper one leaves it ambiguous whether a request to
/// <c>/callback</c> with no trailing slash is covered — on a listener that exists for exactly
/// one request, from a browser, that is not a thing to leave to chance.
/// </para>
/// </remarks>
public static class LoopbackCallback
{
    /// <summary>What <c>HttpListener.Prefixes</c> is given. Ends in a slash; must.</summary>
    public static string PrefixFor(int port) => $"http://127.0.0.1:{port}/";

    /// <summary>What the server is told to send the browser back to. Does not end in a slash.</summary>
    public static string RedirectUriFor(int port) => $"http://127.0.0.1:{port}/callback";
}
