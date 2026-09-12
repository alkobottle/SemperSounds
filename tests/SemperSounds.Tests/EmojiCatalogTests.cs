using SemperSounds.Core.Sounds;
using SemperSounds.Web.Components;

namespace SemperSounds.Tests;

public class EmojiCatalogTests
{
    [Fact]
    public void EverySwatch_SurvivesTheParser()
    {
        // The load-bearing test for the whole catalog. Clicking a swatch calls Select with no
        // validation of its own, so an emoji the parser rejects is stored happily and then
        // read back as the default face — no error anywhere, just the wrong emoji on the tile.
        // Adding one from a block IsPictographic does not cover is exactly that easy.
        foreach (var entry in EmojiCatalog.All)
        {
            Assert.True(SoundEmoji.TryParse(entry.Emoji, out var parsed), $"{entry.Emoji} ({entry.Keywords}) is not accepted");
            Assert.Equal(entry.Emoji, parsed.Raw);
            Assert.Equal(entry.Emoji, SoundEmoji.Normalize(entry.Emoji));
        }
    }

    [Fact]
    public void NoEmoji_AppearsTwice()
    {
        // Duplicates are invisible in a categorised grid — the same face in two sections reads
        // as two choices — and they make the "search finds one row" expectation false.
        var duplicates = EmojiCatalog.All
            .GroupBy(entry => entry.Emoji)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryEntry_IsSearchable()
    {
        // An entry with no keywords can only ever be found by scrolling, which defeats the
        // point of having grown the list.
        Assert.All(EmojiCatalog.All, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Keywords)));
    }

    [Fact]
    public void TheOriginalForty_AreStillOffered()
    {
        // These are what the board already uses. Reorganising the catalog must not quietly
        // retire one, or existing sounds show an emoji nobody can pick again.
        string[] original =
        [
            "🔥", "😂", "💀", "📣", "🎺", "🚨", "🤡", "👏", "🎉", "💥",
            "🙃", "😱", "🤬", "🥁", "🎵", "🔔", "😴", "🫠", "👀", "🧠",
            "🐸", "🦆", "🐄", "🚀", "⚡", "💣", "🍆", "🥴", "😤", "🤔",
            "❤️", "✅", "❌", "⭐", "🎯", "🏆", "🪦", "🫡", "😭", "🙏",
        ];

        Assert.Equal(40, original.Length);

        var offered = EmojiCatalog.All.Select(entry => entry.Emoji).ToHashSet();

        Assert.All(original, emoji => Assert.Contains(emoji, offered));
    }

    [Fact]
    public void Search_MatchesKeywordsAcrossCategories()
    {
        var results = EmojiCatalog.Search("loud");

        Assert.NotEmpty(results);
        Assert.All(results, category => Assert.NotEmpty(category.Entries));
        Assert.Contains(results.SelectMany(c => c.Entries), entry => entry.Emoji == "📣");
    }

    [Fact]
    public void Search_FindsAnEmojiPastedIntoTheBox()
    {
        // How somebody checks whether the one they already have is in the list.
        var results = EmojiCatalog.Search("🔥");

        Assert.Single(results.SelectMany(category => category.Entries));
    }

    [Fact]
    public void SearchingForNothing_ReturnsTheWholeCatalog()
    {
        // The untouched search box is the normal state of the picker; returning nothing for a
        // blank term would render an empty panel on open.
        Assert.Equal(EmojiCatalog.Categories, EmojiCatalog.Search(""));
        Assert.Equal(EmojiCatalog.Categories, EmojiCatalog.Search("   "));
        Assert.Equal(EmojiCatalog.Categories, EmojiCatalog.Search(null));
    }

    [Fact]
    public void AnUnmatchedTerm_LeavesNoEmptyCategories()
    {
        // The picker renders a heading per returned category, so a category kept with zero
        // entries would draw a divider over nothing.
        Assert.Empty(EmojiCatalog.Search("xyzzy"));
    }
}
