using SemperSounds.Web;

namespace SemperSounds.Tests;

public class NavDestinationTests
{
    [Fact]
    public void TheRouteBeingViewed_IsActive()
    {
        Assert.True(NavDestination.IsActive("board", "/board"));
        Assert.True(NavDestination.IsActive("entry-sound", "/entry-sound"));
    }

    [Fact]
    public void EveryOtherRoute_IsNot()
    {
        Assert.False(NavDestination.IsActive("upload", "/board"));
        Assert.False(NavDestination.IsActive("stats", "/log"));
    }

    [Fact]
    public void AChildRoute_KeepsItsParentLit()
    {
        Assert.True(NavDestination.IsActive("admin/entry-sounds", "/admin"));
    }

    [Fact]
    public void ASharedPrefix_IsNotAMatch()
    {
        // "/entry-sound" is a string prefix of "/entry-sounds", so a StartsWith would light
        // the Entry tab on a route that has nothing to do with it. Matching is on segment
        // boundaries precisely so adding such a route later cannot cause that quietly.
        Assert.False(NavDestination.IsActive("entry-sounds", "/entry-sound"));
        Assert.False(NavDestination.IsActive("boardgames", "/board"));
    }

    [Fact]
    public void Root_OnlyEverMatchesItself()
    {
        // Every path starts with "/", so a prefix rule would leave a Landing entry lit on
        // every page of the app.
        Assert.True(NavDestination.IsActive("", "/"));
        Assert.False(NavDestination.IsActive("board", "/"));
    }

    [Theory]
    [InlineData("board")]
    [InlineData("/board")]
    [InlineData("board/")]
    [InlineData("board?sort=Name")]
    [InlineData("board#tiles")]
    public void SlashesAndQueryStrings_DoNotChangeTheAnswer(string relativePath)
    {
        // ToBaseRelativePath hands back no leading slash while the markup writes one, and the
        // board puts its own state in the query string.
        Assert.True(NavDestination.IsActive(relativePath, "/board"));
    }

    [Fact]
    public void TheHistoryMenu_IsActiveForEitherChild()
    {
        // The activator has no Href, so it can only be lit from what it contains.
        Assert.True(NavDestination.IsGroupActive("log", "/log", "/stats"));
        Assert.True(NavDestination.IsGroupActive("stats", "/log", "/stats"));
        Assert.False(NavDestination.IsGroupActive("board", "/log", "/stats"));
    }

    [Fact]
    public void TheClassHelpers_RenderNothingWhenInactive()
    {
        // Razor writes a null Class as no attribute at all; an empty string would emit
        // class="" and an "active" literal would style every destination at once.
        Assert.Equal(NavDestination.ActiveClass, NavDestination.ClassFor("board", "/board"));
        Assert.Null(NavDestination.ClassFor("upload", "/board"));
        Assert.Equal(NavDestination.ActiveClass, NavDestination.ClassForGroup("stats", "/log", "/stats"));
        Assert.Null(NavDestination.ClassForGroup("board", "/log", "/stats"));
    }
}
