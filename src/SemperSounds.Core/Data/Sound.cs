namespace SemperSounds.Core.Data;

/// <summary>A clip in the soundboard library.</summary>
public sealed class Sound
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Display name shown on the tile.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The tile's emoji, in Discord's canonical format: the character itself for standard
    /// emoji, or &lt;:name:id&gt; for one of the server's custom ones. Never empty —
    /// see <see cref="Sounds.SoundEmoji.Normalize"/>.
    /// </summary>
    public string Emoji { get; set; } = Sounds.SoundEmoji.DefaultEmoji;

    /// <summary>
    /// Comma-separated tags, lowercased. The library is small enough (hundreds, not
    /// millions) that filtering in memory beats the ceremony of a join table.
    /// </summary>
    public string Tags { get; set; } = string.Empty;

    public int DurationMs { get; set; }

    /// <summary>Discord user ID of whoever uploaded it.</summary>
    public required ulong UploaderId { get; set; }

    /// <summary>Uploader's display name at upload time, so the UI needs no Discord lookup.</summary>
    public required string UploaderName { get; set; }

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Raw 48 kHz stereo s16le PCM, read directly into the mixer at play time.</summary>
    public string PcmFileName => $"{Id}.pcm";

    /// <summary>Normalized mp3 used for in-browser preview, so preview matches what the channel hears.</summary>
    public string PreviewFileName => $"{Id}.mp3";

    public IEnumerable<string> TagList => Tags
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
