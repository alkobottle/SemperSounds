using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;

namespace SemperSounds.Core.Preferences;

/// <summary>
/// Reads and writes each user's settings, so they follow the person rather than the browser.
/// </summary>
/// <remarks>
/// The store is deliberately ignorant of what it holds: callers hand it a string and get one
/// back. That keeps the serialisation — and the rules about tolerating an unreadable or
/// newer shape — with the layer that owns the setting.
///
/// Every method takes the user id from its caller, which must have taken it from the
/// authenticated principal and never from anything the client sent. That is the same
/// contract <see cref="Sounds.FavoriteLibrary"/> and <c>EntrySoundLibrary</c> work under.
/// </remarks>
public sealed class UserPreferenceStore(SoundboardDbContext db)
{
    /// <summary>The stored value, or null when this user has never saved one.</summary>
    /// <remarks>
    /// Null means "nothing saved", which callers must tell apart from "saved as empty": the
    /// first is a new user who should get defaults, and on the board it is also what triggers
    /// adopting whatever the browser still has from before this table existed.
    /// </remarks>
    public Task<string?> GetAsync(ulong userId, string key, CancellationToken cancellationToken = default) =>
        db.Set<UserPreference>().AsNoTracking()
            .Where(p => p.UserId == userId && p.Key == key)
            .Select(p => p.Value)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Stores this user's value for the key, replacing any previous one. False when it did
    /// not stick.
    /// </summary>
    /// <remarks>
    /// Returning false rather than throwing, because neither way of failing is worth taking a
    /// page down over — a preference that does not persist leaves the screen in front of the
    /// viewer correct, it just will not survive a reload. Both are reachable without anyone
    /// having written a bug: an oversized value from a user who has selected a great many
    /// tags, and a unique-index clash from two tabs saving at the same moment, where the
    /// other tab's write is as good as this one.
    ///
    /// A missing key is the exception and still throws. Nothing a user does can cause it, so
    /// it can only be a caller that was never finished.
    /// </remarks>
    public async Task<bool> SaveAsync(ulong userId, string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        // Refused rather than truncated: a clipped JSON blob does not fail to load, it fails
        // to parse, and the board answers that with defaults — so truncating would read back
        // as "you never had any preferences" instead of as anything anyone could act on.
        if (value.Length > UserPreference.MaxValueLength) return false;

        var existing = await db.Set<UserPreference>()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key, cancellationToken);

        if (existing is null)
        {
            db.Add(new UserPreference { UserId = userId, Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}
