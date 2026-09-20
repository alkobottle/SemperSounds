using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace SemperSounds.Web.Services;

/// <summary>An approved-but-not-yet-collected pairing.</summary>
public sealed record PendingDeviceCode(ulong UserId, string UserName, string DeviceName);

/// <summary>
/// Holds the one-time codes handed to a desktop client's loopback callback during pairing.
/// </summary>
/// <remarks>
/// <para>
/// A code crosses the browser instead of the token itself, so nothing that acts as the user
/// ever reaches browser history, a bookmark or a reverse-proxy access log. The code is
/// redeemed once, over a direct request from the app, and is worthless afterwards.
/// </para>
/// <para>
/// In memory rather than in the database on purpose. These live for a minute, are read exactly
/// once, and are worth nothing after a restart — a restart during pairing should make the user
/// try again, not leave a redeemable row behind. That also means a multi-instance deployment
/// would need revisiting; this app is deliberately one process.
/// </para>
/// <para>
/// The verifier is the same idea as PKCE, for the same reason: any local process can watch the
/// loopback port and race the real client for the code, and only the app that started the flow
/// knows the verifier behind the challenge.
/// </para>
/// </remarks>
public sealed class DeviceCodeStore(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> _codes = new(StringComparer.Ordinal);

    /// <summary>
    /// How long an approved code stays redeemable. Short because the app is already waiting on
    /// its loopback listener by the time this is minted — the slow part of pairing, which can
    /// include a whole Discord login, happens before approval and therefore before the clock
    /// starts.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    /// <summary>For tests: how many codes are still held.</summary>
    public int PendingCount => _codes.Count;

    /// <summary>The challenge a client must have published for <paramref name="verifier"/>.</summary>
    public static string ChallengeFor(string verifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

    public string Issue(ulong userId, string userName, string deviceName, string redirectUri, string codeChallenge)
    {
        Prune();

        var code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        _codes[code] = new Entry(
            new PendingDeviceCode(userId, userName, deviceName), redirectUri, codeChallenge, _time.GetUtcNow().Add(Lifetime));

        return code;
    }

    /// <summary>
    /// Consumes the code and returns who approved it, or null when it is unknown, expired, or
    /// presented with the wrong verifier or redirect URI.
    /// </summary>
    public PendingDeviceCode? Redeem(string? code, string? verifier, string? redirectUri)
    {
        if (string.IsNullOrEmpty(code) || !_codes.TryGetValue(code, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= _time.GetUtcNow())
        {
            _codes.TryRemove(code, out _);
            return null;
        }

        // Checked before the code is consumed: burning it on a failed attempt would let anyone
        // who can guess a code deny the real client its pairing.
        if (verifier is null ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(ChallengeFor(verifier)), Encoding.UTF8.GetBytes(entry.CodeChallenge)) ||
            !string.Equals(redirectUri, entry.RedirectUri, StringComparison.Ordinal))
        {
            return null;
        }

        return _codes.TryRemove(code, out var taken) ? taken.Pending : null;
    }

    /// <summary>
    /// Drops expired entries. Called on every issue because nothing else ever walks this
    /// dictionary, and without it an authenticated endpoint feeds an unbounded collection.
    /// </summary>
    private void Prune()
    {
        var now = _time.GetUtcNow();
        foreach (var (code, entry) in _codes)
        {
            if (entry.ExpiresAt <= now)
            {
                _codes.TryRemove(code, out _);
            }
        }
    }

    private sealed record Entry(PendingDeviceCode Pending, string RedirectUri, string CodeChallenge, DateTimeOffset ExpiresAt);
}
