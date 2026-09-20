using SemperSounds.Web.Services;

namespace SemperSounds.Tests;

/// <summary>
/// The one place in device pairing where getting it wrong hands somebody else's credential
/// away. <c>/device/authorize</c> redirects the signed-in user's browser to whatever
/// <c>redirect_uri</c> it was given, carrying a code that can be exchanged for a token — so
/// anything but a literal loopback address is an open redirect with a credential attached.
/// </summary>
/// <remarks>
/// The rejections are the point. Each one below is a real way a URL can look local and not be,
/// and none of them fails loudly on its own: they all just work, for the attacker.
/// </remarks>
public class LoopbackRedirectTests
{
    [Theory]
    [InlineData("http://127.0.0.1:49152/callback")]
    [InlineData("http://127.0.0.1:1024/")]
    [InlineData("http://127.0.0.1:65535/cb")]
    [InlineData("http://[::1]:49152/callback")]
    public void LiteralLoopback_IsAllowed(string uri) => Assert.True(LoopbackRedirect.IsAllowed(uri));

    [Fact]
    public void Localhost_IsRejected()
    {
        // RFC 8252 prefers the literals: "localhost" resolves through the host's name
        // resolution, which a hosts-file entry or a hostile DNS answer can move elsewhere.
        Assert.False(LoopbackRedirect.IsAllowed("http://localhost:49152/callback"));
    }

    [Theory]
    [InlineData("http://127.0.0.1@evil.test/cb")]          // userinfo: the real host is evil.test
    [InlineData("http://evil.test/cb")]
    [InlineData("http://evil.test:49152/cb?x=http://127.0.0.1")]
    [InlineData("http://127.0.0.1.evil.test:49152/cb")]    // a subdomain that merely starts with it
    public void SomethingThatMerelyLooksLocal_IsRejected(string uri) =>
        Assert.False(LoopbackRedirect.IsAllowed(uri));

    [Theory]
    [InlineData("http://0.0.0.0:49152/cb")]                // binds everywhere, not loopback
    [InlineData("http://127.1:49152/cb")]                  // shorthand some parsers expand
    [InlineData("http://2130706433:49152/cb")]             // 127.0.0.1 as a single integer
    [InlineData("http://0x7f.0.0.1:49152/cb")]
    public void NonCanonicalFormsOfLoopback_AreRejected(string uri) =>
        Assert.False(LoopbackRedirect.IsAllowed(uri));

    [Theory]
    [InlineData("https://127.0.0.1:49152/cb")]             // the client's HttpListener is plain http
    [InlineData("ftp://127.0.0.1:49152/cb")]
    [InlineData("file:///c:/windows/system32")]
    [InlineData("javascript:alert(1)")]
    public void AnyOtherScheme_IsRejected(string uri) => Assert.False(LoopbackRedirect.IsAllowed(uri));

    [Theory]
    [InlineData("//evil.test/cb")]                         // scheme-relative: the browser fills in https:
    [InlineData("/cb")]
    [InlineData("cb")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnythingNotAbsolute_IsRejected(string? uri) => Assert.False(LoopbackRedirect.IsAllowed(uri));

    [Fact]
    public void APortBelowTheEphemeralRange_IsRejected()
    {
        // The client asks the OS for a free port, which is always high. A low one means
        // somebody wrote the URL by hand.
        Assert.False(LoopbackRedirect.IsAllowed("http://127.0.0.1:80/cb"));
        Assert.False(LoopbackRedirect.IsAllowed("http://127.0.0.1/cb"));
    }

    [Fact]
    public void Garbage_DoesNotThrow()
    {
        // Reached by anything hitting the page with a hand-written query string.
        Assert.False(LoopbackRedirect.IsAllowed("http://"));
        Assert.False(LoopbackRedirect.IsAllowed(new string('x', 5000)));
        Assert.False(LoopbackRedirect.IsAllowed("http://127.0.0.1:99999999/cb"));
    }
}
