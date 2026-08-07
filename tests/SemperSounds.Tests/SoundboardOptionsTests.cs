using SemperSounds.Core.Configuration;

namespace SemperSounds.Tests;

public class SoundboardOptionsTests
{
    [Fact]
    public void RelativeDataPath_StillYieldsRootedPaths()
    {
        // Development config uses "./data". A relative path leaks into Results.File(),
        // which treats non-rooted paths as virtual paths under wwwroot rather than as
        // files on disk -- so previews 404 even though the file is plainly there.
        var options = new SoundboardOptions { DataPath = "./data" };

        Assert.True(Path.IsPathRooted(options.SoundsPath), $"SoundsPath was '{options.SoundsPath}'");
        Assert.True(Path.IsPathRooted(options.DatabasePath), $"DatabasePath was '{options.DatabasePath}'");
    }

    [Fact]
    public void AbsoluteDataPath_IsPreserved()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "sempersounds");
        var options = new SoundboardOptions { DataPath = absolute };

        Assert.Equal(Path.Combine(absolute, "sounds"), options.SoundsPath);
        Assert.Equal(Path.Combine(absolute, "sempersounds.db"), options.DatabasePath);
    }
}
