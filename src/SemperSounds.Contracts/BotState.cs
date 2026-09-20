namespace SemperSounds.Contracts;

/// <summary>
/// What the tray icon needs to know, answered for one particular person.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately per-connection rather than a single broadcast value. The question the client
/// actually asks is "can I press a key right now", and that depends on where <em>you</em> are
/// sitting, which only the voice state tracker knows.
/// </para>
/// <para>
/// It also deliberately carries nothing about mutes, deafens or cameras. Voice state changes
/// for all of those, so a state that ignored them lets the broadcaster collapse the whole
/// class of event to zero messages by simply comparing against what it last sent.
/// </para>
/// </remarks>
/// <param name="IsBotReady">
/// The gateway is connected and usable. Distinguishes "the server is up but Discord is not
/// ready yet" from "the bot is idle" — identical from the outside, and the first one resolves
/// itself while the second needs the user to act.
/// </param>
public sealed record BotState(
    bool IsBotReady, bool IsBotConnected, string BotChannelName, bool IsBotInYourChannel, bool AreYouInVoice)
{
    public static BotState Unknown { get; } = new(false, false, string.Empty, false, false);
}
