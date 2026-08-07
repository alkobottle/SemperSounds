using Microsoft.Extensions.Options;
using SemperSounds.Core.Audio;
using SemperSounds.Core.Configuration;

namespace SemperSounds.Core.Sounds;

/// <param name="IsValid">Whether the upload may be stored.</param>
/// <param name="Error">User-facing reason for rejection, empty when valid.</param>
/// <param name="DurationMs">Probed duration, meaningful only when valid.</param>
public readonly record struct UploadValidationResult(bool IsValid, string Error, int DurationMs)
{
    public static UploadValidationResult Valid(int durationMs) => new(true, string.Empty, durationMs);
    public static UploadValidationResult Invalid(string error) => new(false, error, 0);
}

/// <summary>
/// Enforces the soundboard's upload rules: it must be audio, and it must be short.
/// </summary>
public sealed class UploadValidator(IAudioProbe probe, IOptions<SoundboardOptions> options)
{
    private readonly SoundboardOptions _options = options.Value;

    public async Task<UploadValidationResult> ValidateAsync(
        string filePath,
        long fileSizeBytes,
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

        var seconds = probeResult.Duration.TotalSeconds;

        if (seconds > _options.MaxDurationSeconds + _options.DurationToleranceSeconds)
        {
            return UploadValidationResult.Invalid(
                $"That clip is {seconds:0.#} seconds long. The limit is {_options.MaxDurationSeconds:0.#} seconds — trim it and try again.");
        }

        return UploadValidationResult.Valid((int)Math.Round(probeResult.Duration.TotalMilliseconds));
    }
}
