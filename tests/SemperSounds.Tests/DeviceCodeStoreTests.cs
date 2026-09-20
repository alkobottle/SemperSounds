using SemperSounds.Web.Services;

namespace SemperSounds.Tests;

/// <summary>
/// The short-lived code that crosses the browser during pairing.
/// </summary>
/// <remarks>
/// A code rather than the token itself, so nothing that acts as the user ever lands in browser
/// history or a proxy log. That only helps if the code is genuinely single-use and genuinely
/// short-lived, and if presenting it proves you are the app that started the flow — which is
/// what the verifier is for. Another local process can see the loopback callback; it cannot
/// see the verifier, which never leaves the app.
/// </remarks>
public class DeviceCodeStoreTests
{
    private const ulong Alice = 1001;
    private const string Redirect = "http://127.0.0.1:49152/callback";
    private const string Verifier = "a-high-entropy-verifier";

    private static readonly string Challenge = DeviceCodeStore.ChallengeFor(Verifier);

    private static (DeviceCodeStore Store, FakeTimeProvider Time) New()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        return (new DeviceCodeStore(time), time);
    }

    private static string Issue(DeviceCodeStore store) =>
        store.Issue(Alice, "alice", "Desktop", Redirect, Challenge);

    [Fact]
    public void AFreshCode_Redeems()
    {
        var (store, _) = New();
        var code = Issue(store);

        var pending = store.Redeem(code, Verifier, Redirect);

        Assert.NotNull(pending);
        Assert.Equal(Alice, pending.UserId);
        Assert.Equal("Desktop", pending.DeviceName);
    }

    [Fact]
    public void ACode_RedeemsOnlyOnce()
    {
        // The whole reason it is a code and not a token. A replayable one is a token with
        // extra steps.
        var (store, _) = New();
        var code = Issue(store);

        Assert.NotNull(store.Redeem(code, Verifier, Redirect));
        Assert.Null(store.Redeem(code, Verifier, Redirect));
    }

    [Fact]
    public void AnExpiredCode_DoesNotRedeem()
    {
        var (store, time) = New();
        var code = Issue(store);

        time.Advance(DeviceCodeStore.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(store.Redeem(code, Verifier, Redirect));
    }

    [Fact]
    public void ACodeJustInsideItsLifetime_StillRedeems()
    {
        var (store, time) = New();
        var code = Issue(store);

        time.Advance(DeviceCodeStore.Lifetime - TimeSpan.FromSeconds(1));

        Assert.NotNull(store.Redeem(code, Verifier, Redirect));
    }

    [Fact]
    public void TheWrongVerifier_DoesNotRedeem()
    {
        // A hostile local process can watch the loopback port and race for the code. It cannot
        // produce the verifier, which never leaves the app that generated it.
        var (store, _) = New();
        var code = Issue(store);

        Assert.Null(store.Redeem(code, "not-the-verifier", Redirect));
    }

    [Fact]
    public void AFailedVerifier_DoesNotBurnTheCode()
    {
        // Otherwise anyone who can guess a code can deny the real client its pairing.
        var (store, _) = New();
        var code = Issue(store);

        Assert.Null(store.Redeem(code, "not-the-verifier", Redirect));
        Assert.NotNull(store.Redeem(code, Verifier, Redirect));
    }

    [Fact]
    public void ADifferentRedirectUri_DoesNotRedeem()
    {
        var (store, _) = New();
        var code = Issue(store);

        Assert.Null(store.Redeem(code, Verifier, "http://127.0.0.1:50000/callback"));
    }

    [Fact]
    public void AnUnknownCode_DoesNotRedeem()
    {
        var (store, _) = New();

        Assert.Null(store.Redeem("never-issued", Verifier, Redirect));
        Assert.Null(store.Redeem("", Verifier, Redirect));
    }

    [Fact]
    public void TwoIssues_Differ()
    {
        var (store, _) = New();

        Assert.NotEqual(Issue(store), Issue(store));
    }

    [Fact]
    public void ExpiredCodes_DoNotAccumulate()
    {
        // Nothing else ever visits this dictionary, so without pruning on write it is an
        // unbounded in-memory collection fed by an authenticated endpoint.
        var (store, time) = New();
        for (var i = 0; i < 50; i++)
        {
            Issue(store);
        }

        time.Advance(DeviceCodeStore.Lifetime + TimeSpan.FromSeconds(1));
        Issue(store);

        Assert.Equal(1, store.PendingCount);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
