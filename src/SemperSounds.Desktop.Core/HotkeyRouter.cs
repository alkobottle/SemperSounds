namespace SemperSounds.Desktop.Core;

public enum HotkeyActionKind
{
    None = 0,
    Play,
    StopAll,
    ToggleSummon,
    ToggleMute,
}

/// <param name="SoundName">Carried so a failure can name the clip without another lookup.</param>
public readonly record struct HotkeyAction(HotkeyActionKind Kind, Guid SoundId, string SoundName)
{
    public static HotkeyAction None { get; } = new(HotkeyActionKind.None, Guid.Empty, string.Empty);
}

/// <summary>
/// Decides what a key press means.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the hook that captures the key, because the hook cannot be tested and every
/// rule that could be wrong lives here.
/// </para>
/// <para>
/// <see cref="WatchedChords"/> exists because of a hard constraint on the other side: the hook
/// callback runs on every keystroke on the machine and must return well inside Windows'
/// low-level hook timeout, or Windows quietly unhooks it and every binding stops working with
/// nothing logged. So the callback tests membership of a set rather than walking the config.
/// </para>
/// </remarks>
public sealed class HotkeyRouter(DesktopConfig config)
{
    private readonly Lock _gate = new();

    private DesktopConfig _config = config;
    private HashSet<HotkeyChord> _watched = Watch(config);

    /// <summary>Whether hotkeys are currently suspended.</summary>
    public bool IsMuted { get; private set; }

    /// <summary>Every chord worth waking the app for. Never contains <see cref="HotkeyChord.None"/>.</summary>
    public IReadOnlySet<HotkeyChord> WatchedChords
    {
        get { lock (_gate) { return _watched; } }
    }

    /// <summary>Swaps in a new configuration. Deliberately leaves <see cref="IsMuted"/> alone.</summary>
    /// <remarks>
    /// Saving settings while muted must not quietly start firing clips into voice again — mute
    /// is a live state the user set, not a preference the settings dialog owns.
    /// </remarks>
    public void Update(DesktopConfig updated)
    {
        lock (_gate)
        {
            _config = updated;
            _watched = Watch(updated);
        }
    }

    public HotkeyAction Route(HotkeyChord chord)
    {
        // An unset chord is what every unbound sound and every unassigned special key holds,
        // so matching on it would make one stray key press mean all of them at once.
        if (!chord.IsValid)
        {
            return HotkeyAction.None;
        }

        lock (_gate)
        {
            // Checked before the mute gate, or muting would be a one-way door out of which the
            // only exit is the tray menu.
            if (chord == _config.MuteChord)
            {
                IsMuted = !IsMuted;
                return new HotkeyAction(HotkeyActionKind.ToggleMute, Guid.Empty, string.Empty);
            }

            if (IsMuted)
            {
                return HotkeyAction.None;
            }

            if (chord == _config.StopAllChord)
            {
                return new HotkeyAction(HotkeyActionKind.StopAll, Guid.Empty, string.Empty);
            }

            if (chord == _config.SummonChord)
            {
                return new HotkeyAction(HotkeyActionKind.ToggleSummon, Guid.Empty, string.Empty);
            }

            foreach (var binding in _config.Bindings)
            {
                if (binding.Chord == chord)
                {
                    return new HotkeyAction(HotkeyActionKind.Play, binding.SoundId, binding.CachedName);
                }
            }

            return HotkeyAction.None;
        }
    }

    private static HashSet<HotkeyChord> Watch(DesktopConfig config)
    {
        var watched = new HashSet<HotkeyChord>();

        foreach (var binding in config.Bindings)
        {
            if (binding.Chord.IsValid)
            {
                watched.Add(binding.Chord);
            }
        }

        foreach (var chord in new[] { config.StopAllChord, config.SummonChord, config.MuteChord })
        {
            if (chord.IsValid)
            {
                watched.Add(chord);
            }
        }

        return watched;
    }
}
