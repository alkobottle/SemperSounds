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

    /// <summary>
    /// Longest source file accepted for trimming. Sources may exceed
    /// <see cref="MaxDurationSeconds"/> so long as the kept window does not, but this
    /// stops someone uploading an hour of audio to keep three seconds of it.
    /// </summary>
    [Range(1, 3600)]
    public double MaxSourceDurationSeconds { get; set; } = 300;

    /// <summary>Hard cap on the uploaded file size, before any transcoding.</summary>
    [Range(1024, 104857600)]
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Minimum seconds between plays per user. 0 disables the cooldown.</summary>
    [Range(0, 3600)]
    public int PerUserCooldownSeconds { get; set; }

    /// <summary>Disconnect the bot after this long with nobody else in the channel. 0 disables.</summary>
    [Range(0, 1440)]
    public int IdleLeaveMinutes { get; set; } = 10;

    /// <summary>ffmpeg executable. Bare name resolves via PATH, which is how the container finds it.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>ffprobe executable.</summary>
    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>Upper bound on how long a single ffmpeg/ffprobe run may take before being killed.</summary>
    [Range(1, 300)]
    public int TranscodeTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// <see cref="DataPath"/> resolved to an absolute path.
    /// Development config uses a relative "./data", and a relative path is not merely
    /// untidy: Results.File() treats non-rooted paths as virtual paths under wwwroot,
    /// so previews 404 even though the file is sitting right there on disk.
    /// </summary>
    public string RootedDataPath => Path.GetFullPath(DataPath);

    /// <summary>Directory holding the audio files, derived from <see cref="DataPath"/>.</summary>
    public string SoundsPath => Path.Combine(RootedDataPath, "sounds");

    /// <summary>Full path to the SQLite database file, derived from <see cref="DataPath"/>.</summary>
    public string DatabasePath => Path.Combine(RootedDataPath, "sempersounds.db");
}
