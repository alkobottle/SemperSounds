using SemperSounds.Contracts;

namespace SemperSounds.Desktop.Core;

/// <summary>
/// Decides whether summoning the bot would turn a refused play into a successful one.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of "auto-summon". It lives on the client and not on the server precisely
/// so the hub grants nothing new: joining and then playing is exactly what a person does on
/// the board, in the order they would do it.
/// </para>
/// <para>
/// It answers from <see cref="PlayFailure"/> and never from the message. The strings are prose
/// written for a person to read — "Slow down — 1.2s to go." — and a client branching on their
/// wording starts silently guessing wrong the day somebody rephrases one.
/// </para>
/// </remarks>
public static class AutoSummonPolicy
{
    /// <summary>
    /// True when the press is worth retrying after a join.
    /// </summary>
    /// <param name="enabled">The user's setting. Off means never, whatever the failure was.</param>
    public static bool ShouldSummon(PlayFailure failure, bool enabled) => enabled && failure switch
    {
        // The bot is somewhere the caller is not. Joining moves it to them, which is the one
        // thing that fixes this.
        PlayFailure.BotAbsent or PlayFailure.WrongChannel => true,

        // Joining cannot help: the caller is in no channel for the bot to be summoned to, and
        // JoinAsync would refuse for the same reason.
        PlayFailure.NotInVoice => false,

        // Waiting helps; retrying immediately only earns a second refusal.
        PlayFailure.Cooldown => false,

        // The binding is stale. A join would succeed and the retry would fail identically.
        PlayFailure.Missing => false,

        _ => false,
    };

    /// <summary>
    /// Whether a failure is worth telling the user about.
    /// </summary>
    /// <remarks>
    /// Everything is, including the cooldown. A press that does nothing and says nothing is
    /// indistinguishable from the hook having died — which is a real failure mode here, so the
    /// two must never look alike.
    /// </remarks>
    public static bool ShouldReport(PlayFailure failure) => failure != PlayFailure.None;
}
