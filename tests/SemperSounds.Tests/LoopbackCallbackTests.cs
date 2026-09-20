using SemperSounds.Desktop.Core;
using SemperSounds.Web.Services;

namespace SemperSounds.Tests;

/// <summary>
/// The two spellings of the desktop client's callback address, which are deliberately not the
/// same string and are easy to confuse for each other.
/// </summary>
/// <remarks>
/// <para>
/// An HttpListener prefix must end in a slash — it throws ArgumentException if it does not —
/// while the redirect_uri handed to the server must not, because the server compares it
/// verbatim when the code is redeemed. Writing one where the other belonged crashed the app
/// the moment anybody pressed Pair, from a throw outside any catch.
/// </para>
/// <para>
/// The last test here is the one that matters most: it runs the client's own output through
/// the server's validator, so the two ends cannot drift apart without something failing.
/// </para>
/// </remarks>
public class LoopbackCallbackTests
{
    private const int Port = 49152;

    [Fact]
    public void ThePrefix_EndsInASlash()
    {
        // HttpListener rejects anything else outright, and the throw is an ArgumentException
        // rather than the HttpListenerException a caller would think to guard against.
        Assert.EndsWith("/", LoopbackCallback.PrefixFor(Port), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePrefix_CoversTheCallbackPath()
    {
        // Registered at the root rather than at /callback/, so the listener answers the
        // request whether or not the browser keeps the trailing slash.
        Assert.Equal($"http://127.0.0.1:{Port}/", LoopbackCallback.PrefixFor(Port));
    }

    [Fact]
    public void TheRedirectUri_DoesNotEndInASlash()
    {
        // Compared verbatim by the server when the code is redeemed, so a stray slash here
        // fails the exchange with a message about an invalid code.
        var redirectUri = LoopbackCallback.RedirectUriFor(Port);

        Assert.Equal($"http://127.0.0.1:{Port}/callback", redirectUri);
        Assert.False(redirectUri.EndsWith('/'));
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(49152)]
    [InlineData(65535)]
    public void WhatTheClientSends_IsWhatTheServerAccepts(int port)
    {
        // The cross-check. Either end could be changed on its own and still look correct in
        // isolation; this is what notices when they stop agreeing.
        Assert.True(LoopbackRedirect.IsAllowed(LoopbackCallback.RedirectUriFor(port)));
    }
}
