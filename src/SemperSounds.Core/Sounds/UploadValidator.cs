using Microsoft.Extensions.Options;
using SemperSounds.Core.Audio;
using SemperSounds.Core.Configuration;

namespace SemperSounds.Core.Sounds;

/// <summary>The slice of the source to keep.</summary>
/// <param name="StartSeconds">Offset into the source where the clip begins.</param>
/// <param name="LengthSeconds">How much of the source to keep.</param>
public readonly record struct TrimRequest(double StartSeconds, double LengthSeconds);

/// <param name="IsValid">Whether the upload may be stored.</param>
/// <param name="Error">User-facing reason for rejection, empty when valid.</param>
/// <param name="DurationMs">Duration of the resulting clip, meaningful only when valid.</param>
public readonly record struct UploadValidationResult(bool IsValid, string Error, int DurationMs)
{
    public static UploadValidationResult Valid(int durationMs) => new(true, string.Empty, durationMs);
    public static UploadValidationResult Invalid(string error) => new(false, error, 0);
}

/// <summary>
/// Enforces the soundboard's upload rules: it must be audio, and what gets kept must be
/// short. A long source is fine so long as the window kept from it is not.
/// </summary>
public sealed class UploadValidator(IAudioProbe probe, IOptions<SoundboardOptions> options)
{
    private readonly SoundboardOptions _options = options.Value;

    public async Task<UploadValidationResult> ValidateAsync(
        string filePath,
        long fileSizeBytes,
        TrimRequest? trim = null,
        CancellationToken cancellationToken = default)
    {
        if (fileSizeBytes > _options.MaxUploadBytes)
        {
            var limitMb = _options.MaxUploadBytes / 1024d / 1024d;
            return UploadValidationResult.Invalid(
                $"That file is too large ({fileSizeBytes / 1024d / 1024d:0.#} MB). The limit is {limitMb:0.#} MB.");
        }

        var probeResult = await probe.ProbeAsync(filePath, cancellationToken);

        if (!probeResult.HasAudioStream)
        {
            return UploadValidationResult.Invalid(
                "That file has no audio stream that ffmpeg can read. Upload an mp3, wav, ogg or m4a.");
        }

        var sourceSeconds = probeResult.Duration.TotalSeconds;

        if (sourceSeconds > _options.MaxSourceDurationSeconds)
        {
            return UploadValidationResult.Invalid(
                $"That file is {sourceSeconds / 60:0.#} minutes long. Sources must be under " +
                $"{_options.MaxSourceDurationSeconds / 60:0.#} minutes, even if you only keep a few seconds.");
        }

        var maxKept = _options.MaxDurationSeconds + _options.DurationToleranceSeconds;

        if (trim is not { } window)
        {
            // No trim: the whole file is the clip.
            return sourceSeconds > maxKept
                ? UploadValidationResult.Invalid(
                    $"That clip is {sourceSeconds:0.#} seconds long. Trim it down to " +
                    $"{_options.MaxDurationSeconds:0.#} seconds or less and upload again.")
                : UploadValidationResult.Valid(ToMilliseconds(sourceSeconds));
        }

        if (window.StartSeconds < 0 || window.LengthSeconds <= 0)
        {
            return UploadValidationResult.Invalid("That trim selection is not valid.");
        }

        if (window.LengthSeconds > maxKept)
        {
            return UploadValidationResult.Invalid(
                $"You selected {window.LengthSeconds:0.#} seconds. Keep at most " +
                $"{_options.MaxDurationSeconds:0.#} seconds.");
        }

        if (window.StartSeconds + window.LengthSeconds > sourceSeconds + _options.DurationToleranceSeconds)
        {
            return UploadValidationResult.Invalid("That selection runs past the end of the file.");
        }

        return UploadValidationResult.Valid(ToMilliseconds(window.LengthSeconds));
    }

    private static int ToMilliseconds(double seconds) => (int)Math.Round(seconds * 1000);
}
