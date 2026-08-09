namespace SemperSounds.Core.Data;

public enum SoundboardActivity
{
    Played = 0,
    Joined = 1,
    Left = 2,
}

/// <summary>One thing that happened on the soundboard: a sound played, or the bot summoned
/// to or dropped from a voice channel.</summary>
public sealed class ActivityLogEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public SoundboardActivity Kind { get; set; }

    /// <summary>
    /// The sound played. Null for join and leave. Deliberately NOT a foreign key: anyone
    /// can delete sounds, and the history has to survive that — unlike
    /// <see cref="Favorite"/>, where a dangling reference would be meaningless.
    /// </summary>
    public Guid? SoundId { get; set; }

    /// <summary>Denormalized so the log stays readable after the sound is deleted.</summary>
    public string? SoundName { get; set; }

    /// <summary>Who did it. Null when the bot left on its own because nobody remained.</summary>
    public ulong? UserId { get; set; }

    public string? UserName { get; set; }

    public ulong ChannelId { get; set; }

    /// <summary>Channel name at the time, so a renamed or deleted channel still reads sensibly.</summary>
    public string? ChannelName { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>True when the bot dropped out by itself rather than being told to.</summary>
    public bool IsAutomatic => Kind == SoundboardActivity.Left && UserId is null;
}
