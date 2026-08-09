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
                MaxSourceDurationSeconds = 300,
                MaxUploadBytes = 10 * 1024 * 1024,
            }));

    private static AudioProbeResult Audio(double seconds) => new(true, TimeSpan.FromSeconds(seconds));

    [Fact]
    public async Task ShortClipWithoutTrim_IsAccepted()
    {
        var result = await ValidatorFor(Audio(3.2)).ValidateAsync("clip.mp3", 50_000, trim: null);

        Assert.True(result.IsValid);
        Assert.Equal(3200, result.DurationMs);
    }

    [Fact]
    public async Task LongClipWithoutTrim_IsRejectedAndSaysToTrim()
    {
        // Long sources are no longer refused outright -- they are refused only while
        // untrimmed, which is what points the user at the trimmer.
        var result = await ValidatorFor(Audio(10)).ValidateAsync("long.mp3", 50_000, trim: null);

        Assert.False(result.IsValid);
        Assert.Contains("trim", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LongClipTrimmedToAShortWindow_IsAccepted()
    {
        var trim = new TrimRequest(2.1, 3.0);

        var result = await ValidatorFor(Audio(10.4)).ValidateAsync("long.mp3", 50_000, trim);

        Assert.True(result.IsValid);
        Assert.Equal(3000, result.DurationMs);
    }

    [Fact]
    public async Task TrimWindowLongerThanTheLimit_IsRejected()
    {
        // The browser enforces this too, but a hand-made request must not get through.
        var result = await ValidatorFor(Audio(30)).ValidateAsync("long.mp3", 50_000, new TrimRequest(0, 12));

        Assert.False(result.IsValid);
        Assert.Contains("5", result.Error);
    }

    [Fact]
    public async Task TrimWindowRunningPastTheEnd_IsRejected()
    {
        var result = await ValidatorFor(Audio(4)).ValidateAsync("clip.mp3", 50_000, new TrimRequest(3.0, 4.0));

        Assert.False(result.IsValid);
        Assert.Contains("end", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-1.0, 2.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.0, -2.0)]
    public async Task NonsensicalTrimWindow_IsRejected(double start, double length)
    {
        var result = await ValidatorFor(Audio(10)).ValidateAsync("clip.mp3", 50_000, new TrimRequest(start, length));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task SourceLongerThanTheSourceCap_IsRejectedEvenWithATrim()
    {
        // Guards against someone uploading an hour of audio just to keep three seconds.
        var options = new SoundboardOptions { MaxSourceDurationSeconds = 60 };
        var result = await ValidatorFor(Audio(3600), options).ValidateAsync("podcast.mp3", 50_000, new TrimRequest(0, 3));

        Assert.False(result.IsValid);
        Assert.Contains("long", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClipSlightlyOverLimit_IsAcceptedWithinTolerance()
    {
        // Encoders pad. A clip authored as exactly 5.00s routinely probes at 5.02s.
        Assert.True((await ValidatorFor(Audio(5.02)).ValidateAsync("padded.mp3", 50_000, trim: null)).IsValid);
    }

    [Fact]
    public async Task FileWithNoAudioStream_IsRejected()
    {
        var result = await ValidatorFor(AudioProbeResult.NotAudio).ValidateAsync("virus.mp3", 50_000, trim: null);

        Assert.False(result.IsValid);
        Assert.Contains("audio", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OversizedFile_IsRejected()
    {
        var options = new SoundboardOptions { MaxUploadBytes = 1024 };

        var result = await ValidatorFor(Audio(1.0), options).ValidateAsync("huge.mp3", 5_000_000, trim: null);

        Assert.False(result.IsValid);
        Assert.Contains("large", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
