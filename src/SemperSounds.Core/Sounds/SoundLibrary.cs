using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SemperSounds.Core.Audio;
using SemperSounds.Core.Configuration;
using SemperSounds.Core.Data;

namespace SemperSounds.Core.Sounds;

/// <param name="Sound">The stored sound, null when the upload was rejected.</param>
/// <param name="Error">User-facing rejection reason, empty on success.</param>
public readonly record struct SoundUploadResult(Sound? Sound, string Error)
{
    public bool IsSuccess => Sound is not null;

    public static SoundUploadResult Success(Sound sound) => new(sound, string.Empty);
    public static SoundUploadResult Failure(string error) => new(null, error);
}

/// <summary>
/// Owns the sound library: storing uploads, serving them back, and recording plays.
/// </summary>
public sealed class SoundLibrary(
    SoundboardDbContext db,
    UploadValidator validator,
    IAudioTranscoder transcoder,
    IOptions<SoundboardOptions> options,
    ILogger<SoundLibrary> logger)
{
    private readonly SoundboardOptions _options = options.Value;

    public async Task<SoundUploadResult> AddAsync(
        Stream content,
        string originalFileName,
        string displayName,
        string tags,
        ulong uploaderId,
        string uploaderName,
        string emoji = "",
        TrimRequest? trim = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.SoundsPath);

        var sound = new Sound
        {
            Name = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileNameWithoutExtension(originalFileName)
                : displayName.Trim(),
            Tags = NormalizeTags(tags),
            // Normalized rather than trusted: keeps "every sound has an emoji" true no
            // matter which caller is involved, not only the upload form.
            Emoji = SoundEmoji.Normalize(emoji),
            UploaderId = uploaderId,
            UploaderName = uploaderName,
        };

        // ffprobe needs a real path, so the upload is spooled to a temp file first.
        var tempPath = Path.Combine(_options.SoundsPath, $"{sound.Id}.upload");
        var pcmPath = Path.Combine(_options.SoundsPath, sound.PcmFileName);
        var previewPath = Path.Combine(_options.SoundsPath, sound.PreviewFileName);

        var stored = false;

        try
        {
            long sizeBytes;
            await using (var file = File.Create(tempPath))
            {
                await content.CopyToAsync(file, cancellationToken);
                sizeBytes = file.Length;
            }

            var validation = await validator.ValidateAsync(tempPath, sizeBytes, trim, cancellationToken);
            if (!validation.IsValid)
            {
                return SoundUploadResult.Failure(validation.Error);
            }

            sound.DurationMs = validation.DurationMs;

            await transcoder.TranscodeAsync(
                tempPath, pcmPath, previewPath,
                trim?.StartSeconds ?? 0,
                trim?.LengthSeconds,
                cancellationToken);

            db.Sounds.Add(sound);
            await db.SaveChangesAsync(cancellationToken);
            stored = true;

            logger.LogInformation(
                "{Uploader} uploaded {Name} ({DurationMs}ms)", uploaderName, sound.Name, sound.DurationMs);

            return SoundUploadResult.Success(sound);
        }
        catch (FfmpegException ex)
        {
            logger.LogWarning(ex, "Transcoding {Name} failed", sound.Name);
            return SoundUploadResult.Failure($"Could not process that file: {ex.Message}");
        }
        finally
        {
            // The temp file always goes. So do the converted outputs unless the row was
            // actually committed — otherwise rejected uploads slowly fill the volume.
            DeleteQuietly(tempPath);
            if (!stored)
            {
                DeleteQuietly(pcmPath, previewPath);
            }
        }
    }

    public Task<List<Sound>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.Sounds.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);

    public Task<Sound?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Sounds.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sound = await db.Sounds.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sound is null)
        {
            return false;
        }

        db.Sounds.Remove(sound);
        await db.SaveChangesAsync(cancellationToken);

        DeleteQuietly(
            Path.Combine(_options.SoundsPath, sound.PcmFileName),
            Path.Combine(_options.SoundsPath, sound.PreviewFileName));

        return true;
    }

    /// <summary>Edits a sound's presentation. Open to any signed-in member, like deleting.</summary>
    public async Task<bool> UpdateAsync(
        Guid id, string newName, string newTags, string newEmoji, CancellationToken cancellationToken = default)
    {
        var sound = await db.Sounds.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sound is null || string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        sound.Name = newName.Trim();
        sound.Tags = NormalizeTags(newTags);
        sound.Emoji = SoundEmoji.Normalize(newEmoji);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Reads a sound's raw PCM for the mixer. Null when the file is missing.</summary>
    public async Task<byte[]?> ReadPcmAsync(Sound sound, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_options.SoundsPath, sound.PcmFileName);
        return File.Exists(path)
            ? await File.ReadAllBytesAsync(path, cancellationToken)
            : null;
    }

    public async Task LogPlayAsync(
        Sound sound, ulong userId, string userName, ulong channelId,
        CancellationToken cancellationToken = default)
    {
        db.PlayLog.Add(new PlayLogEntry
        {
            SoundId = sound.Id,
            SoundName = sound.Name,
            UserId = userId,
            UserName = userName,
            ChannelId = channelId,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<List<PlayLogEntry>> GetRecentPlaysAsync(int count, CancellationToken cancellationToken = default) =>
        db.PlayLog.AsNoTracking()
            .OrderByDescending(e => e.PlayedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Every tag in use with how many sounds carry it, most-used first.
    /// Ordering by popularity is what nudges people onto the established tag instead of
    /// coining a near-duplicate; ties fall back to alphabetical so the list is stable.
    /// </summary>
    public async Task<IReadOnlyList<TagUsage>> GetTagUsageAsync(CancellationToken cancellationToken = default)
    {
        // Tags live in a CSV column, so counting happens in memory. The library is
        // hundreds of rows, not millions.
        var allTags = await db.Sounds.AsNoTracking()
            .Select(s => s.Tags)
            .ToListAsync(cancellationToken);

        return [.. allTags
            .SelectMany(TagList.Parse)
            .GroupBy(tag => tag, StringComparer.Ordinal)
            .Select(group => new TagUsage(group.Key, group.Count()))
            .OrderByDescending(usage => usage.Count)
            .ThenBy(usage => usage.Tag, StringComparer.Ordinal)];
    }

    /// <summary>Lowercased, trimmed, de-duplicated, stored as CSV.</summary>
    private static string NormalizeTags(string tags) => TagList.ToCsv(TagList.Parse(tags));

    private void DeleteQuietly(params string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete {Path}", path);
            }
        }
    }
}
