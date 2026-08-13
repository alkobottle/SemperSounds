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
    /// <summary>Loudest a single clip may be scaled to. Above this it only clips harder.</summary>
    private const float MaxGain = 2f;

    private sealed class Voice(byte[] pcm, Guid key, float gain)
    {
        public byte[] Pcm { get; } = pcm;

        /// <summary>Identifies what is playing, so callers can show it as active.</summary>
        public Guid Key { get; } = key;

        /// <summary>Linear multiplier applied to this clip alone before it is summed.</summary>
        public float Gain { get; } = gain;

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

    /// <summary>
    /// Which clips are sounding right now. Derived from the live voice list rather than
    /// tracked separately, so firing one clip twice keeps it active until both finish.
    /// </summary>
    public IReadOnlySet<Guid> ActiveKeys
    {
        get
        {
            lock (_gate)
            {
                return _voices.Select(voice => voice.Key).ToHashSet();
            }
        }
    }

    /// <summary>Starts playing a clip. It mixes with anything already playing.</summary>
    /// <param name="key">Identifies the clip so callers can tell what is sounding.</param>
    /// <param name="gain">
    /// Linear multiplier for this clip alone; 1 leaves it untouched. Clamped to
    /// 0..<see cref="MaxGain"/>, so a nonsense value from configuration cannot turn into
    /// garbage audio. Entry sounds use this to sit under conversation.
    /// </param>
    public void Add(byte[] pcm, Guid key = default, float gain = 1f)
    {
        if (pcm.Length < AudioFormat.BytesPerSample)
        {
            return;
        }

        // NaN fails every comparison, so Math.Clamp would throw rather than fall back.
        var safeGain = float.IsNaN(gain) ? 1f : Math.Clamp(gain, 0f, MaxGain);

        lock (_gate)
        {
            _voices.Add(new Voice(pcm, key, safeGain));
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
                    // Scale this voice on its own, then accumulate in int and saturate.
                    // Casting the sum straight to short wraps (60000 becomes -5536), which
                    // turns loud overlaps into noise, so the single clamp stays after the
                    // sum: gain changes what a voice contributes, never where we saturate.
                    var contribution = voice.Gain == 1f
                        ? remaining[i]
                        : (int)(remaining[i] * voice.Gain);

                    var sum = samples[i] + contribution;
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
