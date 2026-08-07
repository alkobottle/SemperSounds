using System.Runtime.InteropServices;

namespace SemperSounds.Core.Audio;

/// <summary>
/// Sums concurrently playing PCM clips into single 20 ms frames.
/// This is what lets several sounds overlap over one outbound Discord voice stream.
/// </summary>
/// <remarks>
/// Thread-safe: clips are added from Blazor circuits while the playback pump drains
/// frames on its own loop, so every access to the voice list is guarded.
/// </remarks>
public sealed class PcmMixer
{
    private sealed class Voice(byte[] pcm)
    {
        public byte[] Pcm { get; } = pcm;
        public int Position { get; set; }
    }

    private readonly Lock _gate = new();
    private readonly List<Voice> _voices = [];

    /// <summary>Number of clips currently playing.</summary>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _voices.Count;
            }
        }
    }

    /// <summary>Starts playing a clip. It mixes with anything already playing.</summary>
    public void Add(byte[] pcm)
    {
        if (pcm.Length < AudioFormat.BytesPerSample)
        {
            return;
        }

        lock (_gate)
        {
            _voices.Add(new Voice(pcm));
        }
    }

    /// <summary>Silences everything currently playing. Backs the panic button.</summary>
    public void StopAll()
    {
        lock (_gate)
        {
            _voices.Clear();
        }
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with exactly one frame of mixed audio.
    /// Returns true when any clip contributed audio, false when the frame is silence.
    /// </summary>
    public bool MixNextFrame(Span<byte> destination)
    {
        destination.Clear();

        lock (_gate)
        {
            if (_voices.Count == 0)
            {
                return false;
            }

            var samples = MemoryMarshal.Cast<byte, short>(destination);

            // Iterate backwards so finished clips can be removed in the same pass.
            for (var v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                var remaining = MemoryMarshal.Cast<byte, short>(voice.Pcm.AsSpan(voice.Position));
                var count = Math.Min(samples.Length, remaining.Length);

                for (var i = 0; i < count; i++)
                {
                    // Accumulate in int, then saturate. Casting the sum straight to short
                    // wraps (60000 becomes -5536), which turns loud overlaps into noise.
                    var sum = samples[i] + remaining[i];
                    samples[i] = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
                }

                voice.Position += count * AudioFormat.BytesPerSample;

                // Evict when fewer than a whole sample remains, not merely when the
                // position reaches the end: a truncated file can leave one trailing byte
                // that never forms a sample, and position alone would never advance past it.
                if (voice.Pcm.Length - voice.Position < AudioFormat.BytesPerSample)
                {
                    _voices.RemoveAt(v);
                }
            }

            return true;
        }
    }
}
