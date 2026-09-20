using SemperSounds.Contracts;

namespace SemperSounds.Tests;

/// <summary>
/// One projection used by both the hub and the broadcaster, so the state a client is handed
/// on connect cannot disagree with the state it is pushed a second later.
/// </summary>
/// <remarks>
/// The interesting cases are all the ones where something is <em>unknown</em>: a null channel
/// means "not in one", and the client has to be able to tell that apart from "somewhere else",
/// because only one of the two is fixed by summoning the bot.
/// </remarks>
public class BotStateFactoryTests
{
    private const ulong General = 100;
    private const ulong Gaming = 200;

    [Fact]
    public void BotInYourChannel_IsPlayable()
    {
        var state = BotStateFactory.For(botReady: true, botChannelId: General, botChannelName: "General", yourChannelId: General);

        Assert.True(state.IsBotConnected);
        Assert.True(state.IsBotInYourChannel);
        Assert.True(state.AreYouInVoice);
        Assert.Equal("General", state.BotChannelName);
    }

    [Fact]
    public void BotElsewhere_IsConnectedButNotYours()
    {
        var state = BotStateFactory.For(true, botChannelId: Gaming, botChannelName: "Gaming", yourChannelId: General);

        Assert.True(state.IsBotConnected);
        Assert.False(state.IsBotInYourChannel);
        Assert.True(state.AreYouInVoice);
    }

    [Fact]
    public void BotAbsent_ReportsNoChannelName()
    {
        var state = BotStateFactory.For(true, botChannelId: null, botChannelName: "ignored", yourChannelId: General);

        Assert.False(state.IsBotConnected);
        Assert.False(state.IsBotInYourChannel);
        Assert.Equal(string.Empty, state.BotChannelName);
    }

    [Fact]
    public void YouNotInVoice_IsNeverInTheBotsChannel()
    {
        // Guards the obvious bug of comparing two nulls and calling it a match: with the bot
        // also absent, null == null would report you as sitting with it.
        var state = BotStateFactory.For(true, botChannelId: null, botChannelName: "", yourChannelId: null);

        Assert.False(state.AreYouInVoice);
        Assert.False(state.IsBotInYourChannel);
    }

    [Fact]
    public void GatewayNotReady_IsDistinctFromIdle()
    {
        // Both render as "you cannot play", but only one of them resolves itself, so the tray
        // has to be able to say which.
        var state = BotStateFactory.For(botReady: false, botChannelId: null, botChannelName: "", yourChannelId: General);

        Assert.False(state.IsBotReady);
        Assert.False(state.IsBotConnected);
    }
}
