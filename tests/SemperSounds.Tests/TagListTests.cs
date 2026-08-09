using SemperSounds.Core.Sounds;

namespace SemperSounds.Tests;

public class TagListTests
{
    [Fact]
    public void Parse_TrimsLowercasesAndDropsBlanks()
    {
        Assert.Equal(["airplane", "meme"], TagList.Parse("  Airplane , MEME ,, "));
    }

    [Fact]
    public void Parse_DeduplicatesCaseInsensitively()
    {
        // The whole point of the feature is convergence, so "Meme" and "meme" must not
        // survive as two tags.
        Assert.Equal(["meme"], TagList.Parse("Meme, meme, MEME"));
    }

    [Fact]
    public void Parse_OfEmptyInput_IsEmpty()
    {
        Assert.Empty(TagList.Parse(""));
        Assert.Empty(TagList.Parse("   "));
        Assert.Empty(TagList.Parse(null));
    }

    [Fact]
    public void ToCsv_RoundTripsThroughParse()
    {
        var csv = TagList.ToCsv(["Airplane", "meme"]);

        Assert.Equal("airplane,meme", csv);
        Assert.Equal(["airplane", "meme"], TagList.Parse(csv));
    }

    [Fact]
    public void Parse_KeepsInnerSpacesButCollapsesEdges()
    {
        Assert.Equal(["leslie nielsen"], TagList.Parse("  Leslie Nielsen  "));
    }
}
