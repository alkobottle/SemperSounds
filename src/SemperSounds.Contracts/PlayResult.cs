namespace SemperSounds.Contracts;

/// <summary>
/// Why a play was refused, as a value the client can branch on.
/// </summary>
/// <remarks>
/// This exists so the client never has to match on <see cref="PlayResult.Error"/>. Those
/// strings are user-facing prose — "Slow down — 1.2s to go." — and the moment somebody
/// rewords one, a client deciding whether to auto-summon from its text starts guessing wrong
/// with no build error to say so.
/// </remarks>
public enum PlayFailure
{
    None = 0,

    /// <summary>The bot is in no voice channel at all. Summoning it will help.</summary>
    BotAbsent,

    /// <summary>The bot is connected, but somewhere else. Summoning it will help.</summary>
    WrongChannel,

    /// <summary>The caller is in no voice channel. Summoning cannot help.</summary>
    NotInVoice,

    /// <summary>The per-user cooldown has not elapsed. Retrying immediately cannot help.</summary>
    Cooldown,

    /// <summary>The sound or its audio file is gone. The binding is stale.</summary>
    Missing,

    /// <summary>Anything else, including the gateway not being ready yet.</summary>
    Other,
}

/// <summary>
/// The outcome of a hub call that plays or joins.
/// </summary>
/// <remarks>
/// A record class rather than a record struct on purpose: a defaulted struct would read as
/// <c>IsSuccess: false, Failure: None</c>, which is a refusal with no reason — exactly the
/// shape that gets silently swallowed.
/// </remarks>
public sealed record PlayResult(bool IsSuccess, PlayFailure Failure, string Error)
{
    public static PlayResult Ok { get; } = new(true, PlayFailure.None, string.Empty);

    public static PlayResult Fail(PlayFailure failure, string error) => new(false, failure, error);
}
