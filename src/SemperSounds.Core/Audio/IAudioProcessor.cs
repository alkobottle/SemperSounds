namespace SemperSounds.Core.Audio;

/// <summary>What ffprobe reports about an uploaded file.</summary>
/// <param name="HasAudioStream">False when the file is not audio at all (or is unreadable).</param>
/// <param name="Duration">Length of the audio stream.</param>
public readonly record struct AudioProbeResult(bool HasAudioStream, TimeSpan Duration)
{
    public static AudioProbeResult NotAudio => new(false, TimeSpan.Zero);
}

/// <summary>
/// Inspects an audio file. Wraps ffprobe behind an interface so upload validation
/// is testable without spawning processes or shipping binary fixtures.
/// </summary>
public interface IAudioProbe
{
    Task<AudioProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Converts an uploaded file into the two forms the app actually serves:
/// canonical PCM for the mixer and a normalized mp3 for browser preview.
/// </summary>
public interface IAudioTranscoder
{
    /// <summary>
    /// Loudness-normalizes <paramref name="sourcePath"/> and writes both outputs.
    /// Normalizing at upload means one clip can't be ten times louder than the rest,
    /// and playback never has to spawn ffmpeg.
    /// </summary>
    Task TranscodeAsync(
        string sourcePath,
        string pcmDestinationPath,
        string previewDestinationPath,
        CancellationToken cancellationToken = default);
}
