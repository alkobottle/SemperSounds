using SemperSounds.Core.Audio;

namespace SemperSounds.Tests;

/// <summary>
/// The trim is expressed entirely through argument <em>position</em>, and getting it wrong
/// produced a wrong-length file rather than an error, so position is pinned here.
/// </summary>
public class FfmpegArgumentTests
{
    private static List<string> Build(double start, double? length) =>
        FfmpegAudioTranscoder.BuildArguments("in.mp3", "out.pcm", "out.mp3", start, length);

    private static int IndexOf(List<string> args, string flag) => args.IndexOf(flag);

    [Fact]
    public void TrimOptions_ComeBeforeTheInput()
    {
        // After -i they would be OUTPUT options binding only to the next output, which
        // trimmed the pcm correctly and left the mp3 preview running to the end of the
        // source. Measured before the fix: 3.00s pcm against an 8.04s mp3.
        var args = Build(2.1, 3.0);

        var input = IndexOf(args, "-i");

        Assert.InRange(IndexOf(args, "-ss"), 0, input - 1);
        Assert.InRange(IndexOf(args, "-t"), 0, input - 1);
    }

    [Fact]
    public void BothOutputsAreProduced()
    {
        var args = Build(2.1, 3.0);

        Assert.Contains("out.pcm", args);
        Assert.Contains("out.mp3", args);
    }

    [Fact]
    public void NoTrim_EmitsNeitherFlag()
    {
        var args = Build(0, null);

        Assert.DoesNotContain("-ss", args);
        Assert.DoesNotContain("-t", args);
    }

    [Fact]
    public void StartWithoutLength_EmitsOnlySeek()
    {
        var args = Build(1.5, null);

        Assert.Contains("-ss", args);
        Assert.DoesNotContain("-t", args);
    }

    [Fact]
    public void TrimValues_AreFormattedInvariantly()
    {
        // On a German locale the default formatting yields "2,1", which ffmpeg misreads.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var args = Build(2.1, 3.5);

            Assert.Equal("2.1", args[IndexOf(args, "-ss") + 1]);
            Assert.Equal("3.5", args[IndexOf(args, "-t") + 1]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
