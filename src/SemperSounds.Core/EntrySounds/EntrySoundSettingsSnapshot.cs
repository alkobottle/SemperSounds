namespace SemperSounds.Core.EntrySounds;

/// <summary>
/// The entry sound settings as a value, so the policy never sees a tracked entity.
/// </summary>
/// <param name="SnoozedUntil">Null when not snoozed. A past value has simply lapsed.</param>
/// <param name="MaxDurationMs">Longest clip that may be picked, enforced at assignment.</param>
public readonly record struct EntrySoundSettingsSnapshot(
    bool IsEnabled,
    DateTimeOffset? SnoozedUntil,
    int VolumePercent,
    int PerUserCooldownSeconds,
    int MaxDurationMs)
{
    /// <summary>Volume as the linear multiplier <c>PcmMixer.Add</c> expects.</summary>
    public float Gain => Math.Clamp(VolumePercent, 0, 100) / 100f;
}
