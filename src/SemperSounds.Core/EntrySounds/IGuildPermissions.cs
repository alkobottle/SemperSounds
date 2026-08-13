namespace SemperSounds.Core.EntrySounds;

/// <summary>
/// Answers whether someone administrates the Discord guild.
/// </summary>
/// <remarks>
/// Behind an interface for the same reason as the ffmpeg wrappers: the implementation reads
/// NetCord's live guild cache and cannot be tested, while everything that depends on the
/// answer can be. Roles are read per request rather than baked into claims at sign-in,
/// because the auth cookie lasts thirty days and a promotion or demotion in that window
/// must take effect without signing out.
/// </remarks>
public interface IGuildPermissions
{
    /// <returns>
    /// True or false once known; null when the answer is not yet knowable — the gateway is
    /// not ready, the guild is not cached, or the member list has not arrived. Null is
    /// deliberately not "no": callers refuse on it, but may say so differently.
    /// </returns>
    bool? IsAdministrator(ulong userId);
}
