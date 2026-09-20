using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;

namespace SemperSounds.Core.Devices;

/// <summary>Who a validated device token belongs to.</summary>
/// <param name="TokenId">
/// Carried so a revocation can find the live connections holding it. SignalR authenticates
/// once, at negotiate, so without this a revoked token keeps working until the socket drops.
/// </param>
public readonly record struct DeviceIdentity(Guid TokenId, ulong UserId, string UserName);

/// <summary>
/// Issues, validates and withdraws the credentials paired desktop clients hold.
/// </summary>
/// <remarks>
/// <para>
/// The plaintext token exists for exactly one method call. Everything afterwards works from
/// the SHA-256 hash, which is what the unique index is on — so validation is a single indexed
/// lookup, and a database dump hands nobody a working credential.
/// </para>
/// <para>
/// A plain hash with no salt or stretching is the right choice here and not an oversight: the
/// token is 256 bits from a CSPRNG, so there is no dictionary to run against it, and the
/// alternative — a per-row salt — would make the lookup a table scan.
/// </para>
/// <para>
/// Every method takes the acting user from its caller, which must have taken it from the
/// authenticated principal. Hiding the revoke button is not what protects revocation; the
/// check in <see cref="RevokeAsync"/> is. That is the same contract
/// <c>EntrySoundAdmin</c> works under.
/// </para>
/// </remarks>
public sealed class DeviceTokenStore(SoundboardDbContext db, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// How stale <see cref="DeviceToken.LastUsedAt"/> may get before a validation writes it.
    /// A reconnecting client would otherwise write a row per connect for a column nobody reads
    /// more precisely than "recently".
    /// </summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(5);

    /// <summary>Prefix on every token, so one is recognisable in a log or a bug report.</summary>
    private const string Prefix = "ss_";

    /// <summary>
    /// Creates a token for one PC and returns the plaintext — the only time it is ever
    /// available. Throws when the user is already at <see cref="DeviceToken.MaxPerUser"/>.
    /// </summary>
    public async Task<string> IssueAsync(ulong userId, string userName, string deviceName, CancellationToken cancellationToken = default)
    {
        var live = await db.DeviceTokens.CountAsync(d => d.UserId == userId && d.RevokedAt == null, cancellationToken);
        if (live >= DeviceToken.MaxPerUser)
        {
            throw new InvalidOperationException(
                $"You already have {DeviceToken.MaxPerUser} paired devices. Revoke one before pairing another.");
        }

        var token = Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = _time.GetUtcNow();

        db.DeviceTokens.Add(new DeviceToken
        {
            UserId = userId,
            UserName = userName,
            TokenHash = Hash(token),
            DeviceName = Truncate(deviceName),
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now.Add(DeviceToken.Lifetime),
        });

        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    /// <summary>
    /// Who this token belongs to, or null when it is unknown, revoked or expired. Renews a
    /// valid token, so an active device never has to re-pair.
    /// </summary>
    public async Task<DeviceIdentity?> ValidateAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = Hash(token);
        var row = await db.DeviceTokens.FirstOrDefaultAsync(d => d.TokenHash == hash, cancellationToken);

        var now = _time.GetUtcNow();
        if (row is null || row.RevokedAt is not null || row.ExpiresAt <= now)
        {
            return null;
        }

        if (now - row.LastUsedAt >= LastUsedResolution)
        {
            row.LastUsedAt = now;
            row.ExpiresAt = now.Add(DeviceToken.Lifetime);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new DeviceIdentity(row.Id, row.UserId, row.UserName);
    }

    /// <summary>This user's devices, newest first. Revoked ones are included, flagged.</summary>
    public async Task<IReadOnlyList<DeviceToken>> ListAsync(ulong userId, CancellationToken cancellationToken = default) =>
        await db.DeviceTokens.AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Withdraws a token. False when it does not exist, is already revoked, or does not belong
    /// to <paramref name="actingUserId"/>.
    /// </summary>
    public async Task<bool> RevokeAsync(Guid id, ulong actingUserId, CancellationToken cancellationToken = default)
    {
        var row = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (row is null || row.UserId != actingUserId || row.RevokedAt is not null)
        {
            return false;
        }

        row.RevokedAt = _time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string Truncate(string name) =>
        name.Length <= DeviceToken.MaxNameLength ? name : name[..DeviceToken.MaxNameLength];
}
