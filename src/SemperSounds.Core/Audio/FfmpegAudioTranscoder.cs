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
        CancellationToken cancellationToken = default)
    {
        string[] arguments =
        [
            "-y",
            "-i", sourcePath,

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
        ];

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

    /// <summary>ffmpeg's stderr is verbose; the last lines carry the actual error.</summary>
    private static string Tail(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length <= 3 ? string.Join(' ', lines) : string.Join(' ', lines[^3..]);
    }
}
