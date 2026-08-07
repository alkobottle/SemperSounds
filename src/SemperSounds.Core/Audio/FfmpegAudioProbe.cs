using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SemperSounds.Core.Configuration;

namespace SemperSounds.Core.Audio;

/// <summary>Reads duration and audio-stream presence with ffprobe.</summary>
public sealed class FfmpegAudioProbe(
    IOptions<SoundboardOptions> options,
    ILogger<FfmpegAudioProbe> logger) : IAudioProbe
{
    private readonly SoundboardOptions _options = options.Value;

    public async Task<AudioProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string[] arguments =
        [
            "-v", "error",
            "-select_streams", "a:0",          // first audio stream only
            "-show_entries", "stream=duration",
            "-show_entries", "format=duration",
            "-of", "json",
            filePath,
        ];

        var result = await FfmpegRunner.RunAsync(
            _options.FfprobePath,
            arguments,
            TimeSpan.FromSeconds(_options.TranscodeTimeoutSeconds),
            logger,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            // A non-zero exit means ffprobe could not parse the file at all, which for
            // upload purposes is the same answer as "this is not audio".
            logger.LogDebug("ffprobe rejected {Path}: {Error}", filePath, result.StandardError.Trim());
            return AudioProbeResult.NotAudio;
        }

        return Parse(result.StandardOutput);
    }

    private static AudioProbeResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // No entries under "streams" means no audio stream matched a:0 — e.g. a video
        // with no audio track, or a renamed non-media file that ffprobe still opened.
        if (!root.TryGetProperty("streams", out var streams) ||
            streams.ValueKind != JsonValueKind.Array ||
            streams.GetArrayLength() == 0)
        {
            return AudioProbeResult.NotAudio;
        }

        // Prefer the stream's own duration; fall back to the container's, since some
        // formats (notably plain mp3) report duration only at the format level.
        var duration = ReadDuration(streams[0]) ?? (root.TryGetProperty("format", out var format)
            ? ReadDuration(format)
            : null);

        return duration is null
            ? AudioProbeResult.NotAudio
            : new AudioProbeResult(true, TimeSpan.FromSeconds(duration.Value));
    }

    private static double? ReadDuration(JsonElement element) =>
        element.TryGetProperty("duration", out var value) &&
        value.ValueKind == JsonValueKind.String &&
        double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
        seconds > 0
            ? seconds
            : null;
}
