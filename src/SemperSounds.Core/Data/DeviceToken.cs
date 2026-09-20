namespace SemperSounds.Core.Data;

/// <summary>
/// A paired desktop client's long-lived credential, as issued to one person for one PC.
/// </summary>
/// <remarks>
/// <para>
/// Only the <see cref="TokenHash"/> is stored. The plaintext is returned exactly once, at the
/// end of the pairing flow, and never again — a database that can hand out working credentials
/// is a worse failure than a user having to re-pair.
/// </para>
/// <para>
/// <see cref="ExpiresAt"/> is not a formality. Guild membership is checked once, in the OAuth
/// ticket, at pairing time; roles and membership are otherwise read live precisely because a
/// thirty-day cookie would outlive a change. A token with no expiry would outlive the user
/// leaving the Discord server entirely, which is strictly worse than the cookie it is modelled
/// on. It is renewed on use, so an active device never notices.
/// </para>
/// <para>
/// A revoked row is kept rather than deleted, so <c>/devices</c> can still explain what
/// happened and when, and so a hash can never be reissued to something else.
/// </para>
/// </remarks>
public sealed class DeviceToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required ulong UserId { get; set; }

    /// <summary>Denormalized like <see cref="Sound.UploaderName"/>, so the hub need not look it up per call.</summary>
    public required string UserName { get; set; }

    /// <summary>Lowercase hex SHA-256 of the token. Unique in the schema.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Whatever the user typed when approving, so a stale entry is recognisable.</summary>
    public string DeviceName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.Add(Lifetime);

    /// <summary>Set when the user revokes it; the row outlives the revocation.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>How long a token lives without being used. Renewed on every successful validation.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// How many live devices one person may hold. A cap rather than unlimited because nothing
    /// in the pairing flow costs the user anything, so a loop could otherwise fill the table.
    /// </summary>
    public const int MaxPerUser = 10;

    public const int MaxNameLength = 100;

    /// <summary>64 hex characters. Fixed width, so the column is not sized for a guess.</summary>
    public const int HashLength = 64;
}
