using Microsoft.Extensions.Options;
using SemperSounds.Core.Audio;
using SemperSounds.Core.Configuration;
using SemperSounds.Core.Sounds;

namespace SemperSounds.Tests;

public class UploadValidatorTests
{
    private sealed class StubProbe(AudioProbeResult result) : IAudioProbe
    {
        public Task<AudioProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private static UploadValidator ValidatorFor(AudioProbeResult probeResult, SoundboardOptions? options = null) =>
        new(new StubProbe(probeResult),
            Options.Create(options ?? new SoundboardOptions
            {
                MaxDurationSeconds = 5.0,
                DurationToleranceSeconds = 0.25,
                MaxUploadBytes = 10 * 1024 * 1024,
            }));

    private static AudioProbeResult Audio(double seconds) =>
        new(true, TimeSpan.FromSeconds(seconds));

    [Fact]
    public async Task ShortClip_IsAccepted()
    {
        var validator = ValidatorFor(Audio(3.2));

        var result = await validator.ValidateAsync("clip.mp3", fileSizeBytes: 50_000);

        Assert.True(result.IsValid);
        Assert.Equal(3200, result.DurationMs);
    }

    [Fact]
    public async Task LongClip_IsRejectedWithDurationMessage()
    {
        var validator = ValidatorFor(Audio(30));

        var result = await validator.ValidateAsync("podcast.mp3", fileSizeBytes: 50_000);

        Assert.False(result.IsValid);
        Assert.Contains("30", result.Error);
        Assert.Contains("5", result.Error);
    }

    [Fact]
    public async Task ClipAtExactLimit_IsAccepted()
    {
        var validator = ValidatorFor(Audio(5.0));

        var result = await validator.ValidateAsync("exactly-five.mp3", fileSizeBytes: 50_000);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ClipSlightlyOverLimit_IsAcceptedWithinTolerance()
    {
        // Encoders pad. A clip authored as exactly 5.00s routinely probes at 5.02s,
        // and rejecting those would be indistinguishable from a bug to the user.
        var validator = ValidatorFor(Audio(5.02));

        var result = await validator.ValidateAsync("padded.mp3", fileSizeBytes: 50_000);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ClipBeyondTolerance_IsRejected()
    {
        var validator = ValidatorFor(Audio(5.5));

        var result = await validator.ValidateAsync("too-long.mp3", fileSizeBytes: 50_000);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task FileWithNoAudioStream_IsRejected()
    {
        // Doubles as the "is this really audio" check: a renamed .exe has no audio stream,
        // so no separate MIME sniffing is needed.
        var validator = ValidatorFor(AudioProbeResult.NotAudio);

        var result = await validator.ValidateAsync("virus.mp3", fileSizeBytes: 50_000);

        Assert.False(result.IsValid);
        Assert.Contains("audio", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OversizedFile_IsRejectedWithoutProbing()
    {
        var validator = ValidatorFor(Audio(1.0), new SoundboardOptions { MaxUploadBytes = 1024 });

        var result = await validator.ValidateAsync("huge.mp3", fileSizeBytes: 5_000_000);

        Assert.False(result.IsValid);
        Assert.Contains("large", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
