using SemperSounds.Web;

namespace SemperSounds.Tests;

public class BoardPreferencesTests
{
    [Fact]
    public void Preferences_RoundTripThroughJson()
    {
        var original = new BoardPreferences(
            Sort: BoardSort.MostPlayed,
            Filters: BoardFilter.NeverPlayed | BoardFilter.Untagged,
            UploaderId: 1234567890123456789,
            Tags: ["meme", "loud"]);

        var restored = BoardPreferencesJson.Deserialize(BoardPreferencesJson.Serialize(original));

        Assert.Equal(original.Sort, restored.Sort);
        Assert.Equal(original.Filters, restored.Filters);
        Assert.Equal(original.UploaderId, restored.UploaderId);
        Assert.Equal(original.Tags, restored.Tags);
    }

    [Fact]
    public void Snowflake_IsSerialisedAsAString()
    {
        // A Discord snowflake exceeds Number.MAX_SAFE_INTEGER. Stored as a JSON number, any
        // JSON.parse in the browser rounds it silently and the uploader filter then matches
        // nobody — with no error anywhere to explain why the board went empty.
        var json = BoardPreferencesJson.Serialize(
            new BoardPreferences(UploaderId: 1234567890123456789));

        Assert.Contains("\"uploaderId\":\"1234567890123456789\"", json);
    }

    [Fact]
    public void Sort_IsSerialisedByNameNotNumber()
    {
        // Stored JSON outlives the enum. By name, inserting a member is harmless; by number,
        // it would silently repoint somebody's saved choice at a different sort.
        var json = BoardPreferencesJson.Serialize(new BoardPreferences(Sort: BoardSort.RecentlyPlayed));

        Assert.Contains("\"sort\":\"RecentlyPlayed\"", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not json at all")]
    [InlineData("{\"sort\":")]
    [InlineData("[]")]
    [InlineData("null")]
    public void AnythingUnreadable_FallsBackToTodaysBoard(string? stored)
    {
        // This runs in OnAfterRenderAsync, where an escaping exception kills the circuit.
        var restored = BoardPreferencesJson.Deserialize(stored);

        Assert.Equal(BoardSort.Name, restored.Sort);
        Assert.Equal(BoardFilter.None, restored.Filters);
        Assert.Null(restored.UploaderId);
    }

    [Fact]
    public void UnknownSortName_FallsBackToName()
    {
        var restored = BoardPreferencesJson.Deserialize("""{"sort":"ByVibes"}""");

        Assert.Equal(BoardSort.Name, restored.Sort);
    }

    [Fact]
    public void UnknownFilterNames_AreIgnoredRatherThanDiscardingTheRest()
    {
        // A preference saved by a newer build should degrade to the part this one understands.
        var restored = BoardPreferencesJson.Deserialize(
            """{"filters":["Untagged","ByVibes"]}""");

        Assert.Equal(BoardFilter.Untagged, restored.Filters);
    }

    [Fact]
    public void UnparseableSnowflake_IsDroppedRatherThanThrowing()
    {
        var restored = BoardPreferencesJson.Deserialize("""{"uploaderId":"not-a-number"}""");

        Assert.Null(restored.UploaderId);
    }

    [Fact]
    public void DefaultPreferences_SerialiseWithoutAnUploader()
    {
        var json = BoardPreferencesJson.Serialize(new BoardPreferences());

        Assert.Equal(BoardSort.Name, BoardPreferencesJson.Deserialize(json).Sort);
        Assert.Null(BoardPreferencesJson.Deserialize(json).UploaderId);
    }
}
