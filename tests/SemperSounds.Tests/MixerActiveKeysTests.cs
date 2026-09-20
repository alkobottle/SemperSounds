using SemperSounds.Core.Audio;

namespace SemperSounds.Tests;

/// <summary>
/// What the mixer reports as still sounding.
/// </summary>
/// <remarks>
/// <see cref="PcmMixer.ActiveKeys"/> is what stops a held-down or hammered key from stacking a
/// clip on top of itself. The guard in <c>PlaybackService.PlayAsync</c> reads it deliberately
/// in place of <c>PlayingSoundIds</c>, which is a snapshot the pump refreshes a frame later
/// and therefore still reads empty during exactly the rapid presses the guard exists to catch.
/// These pin the two properties that makes it work: it is visible the instant a clip is added,
/// and it is gone once the clip has run out.
/// </remarks>
public class MixerActiveKeysTests
{
    private static readonly Guid Airhorn = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bruh = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Exactly one frame of silence, so a single mix drains it.</summary>
    private static byte[] OneFrame() => new byte[AudioFormat.BytesPerFrame];

    [Fact]
    public void AnAddedSound_IsImmediatelyActive()
    {
        // "Immediately" is the whole point: no frame has been mixed yet, and a second press
        // arriving in that window is the one that used to double up.
        var mixer = new PcmMixer();

        mixer.Add(OneFrame(), Airhorn);

        Assert.Contains(Airhorn, mixer.ActiveKeys);
    }

    [Fact]
    public void AnUnrelatedSound_IsNotActive()
    {
        var mixer = new PcmMixer();
        mixer.Add(OneFrame(), Airhorn);

        Assert.DoesNotContain(Bruh, mixer.ActiveKeys);
    }

    [Fact]
    public void AFinishedSound_StopsBeingActive()
    {
        // Otherwise the guard would be permanent and a clip could be played exactly once.
        var mixer = new PcmMixer();
        mixer.Add(OneFrame(), Airhorn);

        mixer.MixNextFrame(new byte[AudioFormat.BytesPerFrame]);

        Assert.DoesNotContain(Airhorn, mixer.ActiveKeys);
    }

    [Fact]
    public void StopAll_ClearsEverything()
    {
        var mixer = new PcmMixer();
        mixer.Add(OneFrame(), Airhorn);
        mixer.Add(OneFrame(), Bruh);

        mixer.StopAll();

        Assert.Empty(mixer.ActiveKeys);
    }

    [Fact]
    public void TwoDifferentSounds_AreBothActive()
    {
        // The guard is per sound, not global: pressing two different keys must still overlap.
        var mixer = new PcmMixer();

        mixer.Add(OneFrame(), Airhorn);
        mixer.Add(OneFrame(), Bruh);

        Assert.Equal(2, mixer.ActiveKeys.Count);
    }
}
