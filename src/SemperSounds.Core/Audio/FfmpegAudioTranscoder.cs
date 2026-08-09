using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SemperSounds.Core.Configuration;

namespace SemperSounds.Core.Audio;

/// <summary>
/// Produces the two artifacts every sound needs, in one ffmpeg pass:
/// canonical PCM for the mixer and a normalized mp3 for browser preview.
/// </summary>
public sealed class FfmpegAudioTranscoder(
    IOptions<SoundboardOptions> options,
    ILogger<FfmpegAudioTranscoder> logger) : IAudioTranscoder
{
    private readonly SoundboardOptions _options = options.Value;

    /// <summary>
    /// EBU R128 loudness normalization. Everything lands at the same perceived level,
    /// so no single upload can blow out the channel.
    /// </summary>
    private const string LoudnessFilter = "loudnorm=I=-16:TP=-1.5:LRA=11";

    public async Task TranscodeAsync(
        string sourcePath,
        string pcmDestinationPath,
        string previewDestinationPath,
        double startSeconds = 0,
        double? lengthSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = BuildArguments(sourcePath, pcmDestinationPath, previewDestinationPath, startSeconds, lengthSeconds);

        var result = await FfmpegRunner.RunAsync(
            _options.FfmpegPath,
            arguments,
            TimeSpan.FromSeconds(_options.TranscodeTimeoutSeconds),
            logger,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new FfmpegException(
                $"ffmpeg failed to convert the upload (exit {result.ExitCode}): {Tail(result.StandardError)}");
        }

        if (!File.Exists(pcmDestinationPath) || new FileInfo(pcmDestinationPath).Length == 0)
        {
            throw new FfmpegException("ffmpeg reported success but produced no audio data.");
        }
    }

    /// <summary>
    /// Builds the ffmpeg command line. Separated out because the trim is expressed purely
    /// through argument <em>position</em>, which is easy to get wrong and fails silently.
    /// </summary>
    /// <remarks>
    /// Both <c>-ss</c> and <c>-t</c> must precede <c>-i</c>, making them **input** options
    /// that apply to everything read from the file. Placed after <c>-i</c> they become
    /// output options and bind only to the *next* output — with the two outputs here that
    /// trimmed the PCM correctly while leaving the mp3 preview running to the end of the
    /// source. Verified: 10s source, seek 2s, keep 3s produced a 3.00s pcm and an 8.04s mp3.
    /// </remarks>
    internal static List<string> BuildArguments(
        string sourcePath,
        string pcmDestinationPath,
        string previewDestinationPath,
        double startSeconds,
        double? lengthSeconds)
    {
        List<string> arguments = ["-y"];

        // Seeking before -i also means ffmpeg skips rather than decoding and discarding the
        // lead-in. It stays frame-accurate because the output is re-encoded.
        if (startSeconds > 0)
        {
            arguments.AddRange(["-ss", Format(startSeconds)]);
        }

        if (lengthSeconds is { } length)
        {
            arguments.AddRange(["-t", Format(length)]);
        }

        arguments.AddRange(["-i", sourcePath]);

        arguments.AddRange(
        [
            // Output 1: raw PCM in the mixer's format.
            "-map", "0:a:0",
            "-af", LoudnessFilter,
            "-ar", AudioFormat.SampleRate.ToString(),
            "-ac", AudioFormat.Channels.ToString(),
            "-f", "s16le",
            pcmDestinationPath,

            // Output 2: normalized mp3 for the browser preview, so what you hear in the
            // page is what the voice channel hears.
            "-map", "0:a:0",
            "-af", LoudnessFilter,
            "-ar", AudioFormat.SampleRate.ToString(),
            "-ac", AudioFormat.Channels.ToString(),
            "-codec:a", "libmp3lame",
            "-b:a", "128k",
            previewDestinationPath,
        ]);

        return arguments;
    }

    /// <summary>Invariant formatting: ffmpeg will not read "2,1" as two-point-one.</summary>
    private static string Format(double seconds) =>
        seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>ffmpeg's stderr is verbose; the last lines carry the actual error.</summary>
    private static string Tail(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length <= 3 ? string.Join(' ', lines) : string.Join(' ', lines[^3..]);
    }
}
