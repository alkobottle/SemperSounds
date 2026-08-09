using SemperSounds.Core.Audio;

namespace SemperSounds.Tests;

public class PcmMixerTests
{
    /// <summary>Builds a PCM buffer from 16-bit samples, interleaved as the mixer expects.</summary>
    private static byte[] Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), samples[i]);
        }
        return bytes;
    }

    private static short SampleAt(ReadOnlySpan<byte> frame, int index) =>
        BitConverter.ToInt16(frame.Slice(index * 2, 2));

    [Fact]
    public void EmptyMixer_FillsFrameWithSilence()
    {
        var mixer = new PcmMixer();
        var frame = new byte[AudioFormat.BytesPerFrame];
        Array.Fill(frame, (byte)0xAB); // poison, so we detect the mixer not writing at all

        var hadAudio = mixer.MixNextFrame(frame);

        Assert.False(hadAudio);
        Assert.All(frame.ToArray(), b => Assert.Equal(0, b));
    }

    [Fact]
    public void SingleClip_IsCopiedIntoFrame_AndRemainderIsSilence()
    {
        var mixer = new PcmMixer();
        mixer.Add(Pcm(100, -200, 300));
        var frame = new byte[AudioFormat.BytesPerFrame];

        var hadAudio = mixer.MixNextFrame(frame);

        Assert.True(hadAudio);
        Assert.Equal(100, SampleAt(frame, 0));
        Assert.Equal(-200, SampleAt(frame, 1));
        Assert.Equal(300, SampleAt(frame, 2));
        Assert.Equal(0, SampleAt(frame, 3));
        Assert.Equal(0, SampleAt(frame, AudioFormat.SamplesPerFrame - 1));
    }

    [Fact]
    public void TwoClips_AreSummedSampleWise()
    {
        var mixer = new PcmMixer();
        mixer.Add(Pcm(1000, 2000));
        mixer.Add(Pcm(30, -2500));
        var frame = new byte[AudioFormat.BytesPerFrame];

        mixer.MixNextFrame(frame);

        Assert.Equal(1030, SampleAt(frame, 0));
        Assert.Equal(-500, SampleAt(frame, 1));
    }

    [Fact]
    public void LoudClips_ClampInsteadOfWrapping()
    {
        var mixer = new PcmMixer();
        mixer.Add(Pcm(30000, -30000));
        mixer.Add(Pcm(30000, -30000));
        var frame = new byte[AudioFormat.BytesPerFrame];

        mixer.MixNextFrame(frame);

        // Naive (short) casting wraps 60000 to -5536, turning a loud sound into
        // a burst of noise with inverted polarity. It must saturate instead.
        Assert.Equal(short.MaxValue, SampleAt(frame, 0));
        Assert.Equal(short.MinValue, SampleAt(frame, 1));
    }

    [Fact]
    public void ClipLongerThanOneFrame_ResumesWhereItLeftOff()
    {
        // Two frames' worth of samples, each holding its own index so we can tell
        // which part of the clip landed in which frame.
        var samples = new short[AudioFormat.SamplesPerFrame * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(i % 1000);
        }

        var mixer = new PcmMixer();
        mixer.Add(Pcm(samples));
        var frame = new byte[AudioFormat.BytesPerFrame];

        mixer.MixNextFrame(frame);
        Assert.Equal(samples[0], SampleAt(frame, 0));
        Assert.Equal(samples[AudioFormat.SamplesPerFrame - 1], SampleAt(frame, AudioFormat.SamplesPerFrame - 1));

        mixer.MixNextFrame(frame);
        Assert.Equal(samples[AudioFormat.SamplesPerFrame], SampleAt(frame, 0));
        Assert.Equal(samples[^1], SampleAt(frame, AudioFormat.SamplesPerFrame - 1));
    }

    [Fact]
    public void FinishedClip_IsDropped_AndMixerReturnsToSilence()
    {
        var mixer = new PcmMixer();
        mixer.Add(Pcm(1234, 5678));
        var frame = new byte[AudioFormat.BytesPerFrame];

        Assert.True(mixer.MixNextFrame(frame));
        Assert.Equal(0, mixer.ActiveCount);

        var hadAudio = mixer.MixNextFrame(frame);

        Assert.False(hadAudio);
        Assert.Equal(0, SampleAt(frame, 0));
    }

    [Fact]
    public void StopAll_SilencesEverythingImmediately()
    {
        var mixer = new PcmMixer();
        mixer.Add(Pcm(new short[AudioFormat.SamplesPerFrame * 4]));
        mixer.Add(Pcm(new short[AudioFormat.SamplesPerFrame * 4]));
        Assert.Equal(2, mixer.ActiveCount);

        mixer.StopAll();

        var frame = new byte[AudioFormat.BytesPerFrame];
        Assert.Equal(0, mixer.ActiveCount);
        Assert.False(mixer.MixNextFrame(frame));
    }

    [Fact]
    public void ActiveKeys_ReportWhichClipsAreSounding()
    {
        // The UI needs this to show a tile as playing and stop it being retriggered.
        var mixer = new PcmMixer();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        mixer.Add(Pcm(new short[AudioFormat.SamplesPerFrame * 3]), first);
        mixer.Add(Pcm(1234, 5678), second);

        Assert.Contains(first, mixer.ActiveKeys);
        Assert.Contains(second, mixer.ActiveKeys);
        Assert.Equal(2, mixer.ActiveKeys.Count);

        var frame = new byte[AudioFormat.BytesPerFrame];
        mixer.MixNextFrame(frame);

        // The short clip is spent after one frame; the long one is still going.
        Assert.Equal([first], mixer.ActiveKeys);
    }

    [Fact]
    public void SameClipTwice_StaysActiveUntilBothCopiesFinish()
    {
        var mixer = new PcmMixer();
        var key = Guid.NewGuid();

        mixer.Add(Pcm(1234, 5678), key);
        mixer.Add(Pcm(new short[AudioFormat.SamplesPerFrame * 3]), key);

        var frame = new byte[AudioFormat.BytesPerFrame];
        mixer.MixNextFrame(frame);

        Assert.Equal([key], mixer.ActiveKeys);
    }

    [Fact]
    public void StopAll_ClearsActiveKeys()
    {
        var mixer = new PcmMixer();
        mixer.Add(Pcm(new short[AudioFormat.SamplesPerFrame * 4]), Guid.NewGuid());

        mixer.StopAll();

        Assert.Empty(mixer.ActiveKeys);
    }

    [Fact]
    public void ClipWithTrailingOddByte_IsStillEvicted()
    {
        // A truncated or corrupt file can end mid-sample. That last lone byte can never
        // form a sample, so a position-only eviction check would leave the voice stuck
        // in the mix forever, permanently pinning the pump to "playing".
        var mixer = new PcmMixer();
        mixer.Add([0x10, 0x20, 0x30]);
        var frame = new byte[AudioFormat.BytesPerFrame];

        mixer.MixNextFrame(frame);

        Assert.Equal(0, mixer.ActiveCount);
        Assert.False(mixer.MixNextFrame(frame));
    }
}
