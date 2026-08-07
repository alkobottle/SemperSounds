using System.ComponentModel.DataAnnotations;

namespace SemperSounds.Core.Configuration;

/// <summary>
/// Soundboard behaviour knobs. Bound from the "Soundboard" configuration section.
/// </summary>
public sealed class SoundboardOptions
{
    public const string SectionName = "Soundboard";

    /// <summary>
    /// Root directory for persisted state. Holds sounds/ and sempersounds.db.
    /// In the container this is the mounted volume, so it survives restarts.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string DataPath { get; set; } = "/data";

    /// <summary>Uploads longer than this are rejected. The whole point of the app is short clips.</summary>
    [Range(0.5, 60.0)]
    public double MaxDurationSeconds { get; set; } = 5.0;

    /// <summary>
    /// Slack added to <see cref="MaxDurationSeconds"/> before rejecting, so a clip authored as
    /// exactly 5.00s that ffprobe reports as 5.02s (encoder padding) is not rejected.
    /// </summary>
    [Range(0.0, 1.0)]
    public double DurationToleranceSeconds { get; set; } = 0.25;

    /// <summary>Hard cap on the uploaded file size, before any transcoding.</summary>
    [Range(1024, 104857600)]
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Minimum seconds between plays per user. 0 disables the cooldown.</summary>
    [Range(0, 3600)]
    public int PerUserCooldownSeconds { get; set; }

    /// <summary>Disconnect the bot after this long with nobody else in the channel. 0 disables.</summary>
    [Range(0, 1440)]
    public int IdleLeaveMinutes { get; set; } = 10;

    /// <summary>Directory holding the audio files, derived from <see cref="DataPath"/>.</summary>
    public string SoundsPath => Path.Combine(DataPath, "sounds");

    /// <summary>Full path to the SQLite database file, derived from <see cref="DataPath"/>.</summary>
    public string DatabasePath => Path.Combine(DataPath, "sempersounds.db");
}
