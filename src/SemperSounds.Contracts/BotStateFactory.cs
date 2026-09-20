namespace SemperSounds.Contracts;

/// <summary>
/// Builds a <see cref="BotState"/> for one person from the raw facts.
/// </summary>
/// <remarks>
/// Lives here, in the assembly both ends share, so the hub's answer on connect and the
/// broadcaster's answer on every push come from the same function and cannot drift apart.
/// </remarks>
public static class BotStateFactory
{
    /// <param name="botChannelId">Null when the bot is in no voice channel.</param>
    /// <param name="yourChannelId">Null when the caller is in no voice channel.</param>
    public static BotState For(bool botReady, ulong? botChannelId, string botChannelName, ulong? yourChannelId)
    {
        // Comparing the two nullables directly would report a match when both are null, which
        // reads as "you are sitting with the bot" for two people who are nowhere.
        var together = botChannelId is { } bot && yourChannelId == bot;

        return new BotState(
            IsBotReady: botReady,
            IsBotConnected: botChannelId is not null,
            BotChannelName: botChannelId is null ? string.Empty : botChannelName,
            IsBotInYourChannel: together,
            AreYouInVoice: yourChannelId is not null);
    }
}
