using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;

namespace SemperSounds.Core.Sounds;

/// <summary>
/// Records what happened on the soundboard: sounds played, and the bot being summoned to
/// or dropping out of a voice channel.
/// </summary>
/// <remarks>
/// Separate from <see cref="SoundLibrary"/>, which already owns uploading, deletion, tags
/// and PCM reading. This class only ever appends and reads back newest-first; grouped reads
/// over the same table — play counts, top sounds, plays per day — live in
/// <c>PlayStatistics</c>, which is why the table now carries a second index.
/// </remarks>
public sealed class ActivityLog(SoundboardDbContext db)
{
    public Task LogPlayAsync(
        Sound sound, ulong userId, string userName, ulong channelId, string? channelName,
        CancellationToken cancellationToken = default) =>
        AppendAsync(new ActivityLogEntry
        {
            Kind = SoundboardActivity.Played,
            SoundId = sound.Id,
            SoundName = sound.Name,
            UserId = userId,
            UserName = userName,
            ChannelId = channelId,
            ChannelName = channelName,
        }, cancellationToken);

    /// <summary>An entry sound that fired because <paramref name="userId"/> walked in.</summary>
    public Task LogEntrySoundAsync(
        Sound sound, ulong userId, string userName, ulong channelId, string? channelName,
        CancellationToken cancellationToken = default) =>
        AppendAsync(new ActivityLogEntry
        {
            Kind = SoundboardActivity.EntryPlayed,
            SoundId = sound.Id,
            SoundName = sound.Name,
            UserId = userId,
            UserName = userName,
            ChannelId = channelId,
            ChannelName = channelName,
        }, cancellationToken);

    public Task LogJoinAsync(
        ulong userId, string userName, ulong channelId, string? channelName,
        CancellationToken cancellationToken = default) =>
        AppendAsync(new ActivityLogEntry
        {
            Kind = SoundboardActivity.Joined,
            UserId = userId,
            UserName = userName,
            ChannelId = channelId,
            ChannelName = channelName,
        }, cancellationToken);

    /// <param name="userId">Null when the bot left on its own because nobody remained.</param>
    public Task LogLeaveAsync(
        ulong? userId, string? userName, ulong channelId, string? channelName,
        CancellationToken cancellationToken = default) =>
        AppendAsync(new ActivityLogEntry
        {
            Kind = SoundboardActivity.Left,
            UserId = userId,
            UserName = userName,
            ChannelId = channelId,
            ChannelName = channelName,
        }, cancellationToken);

    public Task<List<ActivityLogEntry>> GetRecentAsync(int count, CancellationToken cancellationToken = default) =>
        db.ActivityLog.AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(count)
            .ToListAsync(cancellationToken);

    private async Task AppendAsync(ActivityLogEntry entry, CancellationToken cancellationToken)
    {
        db.ActivityLog.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}
