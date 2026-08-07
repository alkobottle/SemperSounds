namespace SemperSounds.Core.Audio;

/// <summary>
/// The single canonical audio format used everywhere downstream of upload:
/// 48 kHz, stereo, signed 16-bit little-endian PCM. This is what Discord's Opus
/// encoder expects, so uploads are converted to it once and never again.
/// </summary>
public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int BytesPerSample = 2;

    /// <summary>Opus frame duration. 20 ms is the standard Discord voice frame.</summary>
    public const int FrameMilliseconds = 20;

    /// <summary>Samples per channel in one frame: 960.</summary>
    public const int SamplesPerChannelPerFrame = SampleRate / 1000 * FrameMilliseconds;

    /// <summary>Total interleaved samples in one frame: 1920.</summary>
    public const int SamplesPerFrame = SamplesPerChannelPerFrame * Channels;

    /// <summary>Bytes in one frame: 3840.</summary>
    public const int BytesPerFrame = SamplesPerFrame * BytesPerSample;
}
