using SemperSounds.Core.Sounds;

namespace SemperSounds.Tests;

public class SoundEmojiTests
{
    [Fact]
    public void StandardEmoji_IsPassedThrough()
    {
        var emoji = SoundEmoji.Parse("🔥");

        Assert.False(emoji.IsCustom);
        Assert.Equal("🔥", emoji.Display);
        Assert.Null(emoji.ImageUrl);
    }

    [Fact]
    public void CustomEmoji_ExposesNameAndCdnUrl()
    {
        var emoji = SoundEmoji.Parse("<:kekw:1234567890>");

        Assert.True(emoji.IsCustom);
        Assert.Equal("kekw", emoji.Name);
        Assert.Equal("https://cdn.discordapp.com/emojis/1234567890.png", emoji.ImageUrl);
    }

    [Fact]
    public void AnimatedCustomEmoji_UsesGifUrl()
    {
        var emoji = SoundEmoji.Parse("<a:party:987>");

        Assert.True(emoji.IsCustom);
        Assert.Equal("party", emoji.Name);
        Assert.Equal("https://cdn.discordapp.com/emojis/987.gif", emoji.ImageUrl);
    }

    [Fact]
    public void CustomEmoji_FallsBackToColonNameWhenImageIsUnavailable()
    {
        // The emoji can be deleted from the server after a sound is tagged with it.
        // The stored name is what keeps the tile readable rather than blank.
        var emoji = SoundEmoji.Parse("<:kekw:1234567890>");

        Assert.Equal(":kekw:", emoji.Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<:broken>")]
    [InlineData("<:name:notanumber>")]
    [InlineData("just some text")]
    public void MalformedInput_IsRejected(string input)
    {
        Assert.False(SoundEmoji.TryParse(input, out _));
    }

    [Fact]
    public void Default_IsUsedWhenNothingIsSupplied()
    {
        // Emoji is required, so the invariant is upheld here rather than only in the form.
        Assert.Equal(SoundEmoji.DefaultEmoji, SoundEmoji.Normalize(""));
        Assert.Equal(SoundEmoji.DefaultEmoji, SoundEmoji.Normalize("   "));
        Assert.Equal(SoundEmoji.DefaultEmoji, SoundEmoji.Normalize("not an emoji"));
        Assert.Equal("🔥", SoundEmoji.Normalize("🔥"));
    }

    [Theory]
    [InlineData("⏰")]   // 0x23F0, misc technical
    [InlineData("⌛")]   // 0x231B
    [InlineData("⏸️")]
    [InlineData("Ⓜ️")]   // 0x24C2
    [InlineData("⤴️")]   // 0x2934
    [InlineData("〽️")]   // 0x303D
    [InlineData("㊙️")]   // 0x3299
    [InlineData("◼️")]   // 0x25FC
    public void EmojiInTheStragglerBlocks_AreAccepted(string input)
    {
        // These sit in blocks that hold a handful of emoji among non-emoji, and each was
        // missing from IsPictographic. Nothing rejects an unaccepted emoji loudly — it is
        // normalised to the default face on the way out, so the only symptom is the wrong
        // picture on a tile.
        Assert.True(SoundEmoji.TryParse(input, out var emoji));
        Assert.Equal(input, emoji.Raw);
    }

    [Fact]
    public void OrdinaryTextFromThoseBlocks_IsStillRejected()
    {
        // Widening the ranges must not turn arbitrary symbols into valid emoji.
        Assert.False(SoundEmoji.TryParse("hello", out _));
        Assert.False(SoundEmoji.TryParse("123", out _));
    }

    [Fact]
    public void SearchText_IncludesCustomEmojiName()
    {
        // A custom emoji cannot be typed into a search box, so its name has to match
        // or custom emoji end up decorative rather than findable.
        Assert.Contains("kekw", SoundEmoji.Parse("<:kekw:123>").SearchText);
        Assert.Contains("🔥", SoundEmoji.Parse("🔥").SearchText);
    }
}
