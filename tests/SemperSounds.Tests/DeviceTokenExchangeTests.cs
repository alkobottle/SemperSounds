using System.Text.Json;
using SemperSounds.Contracts;

namespace SemperSounds.Tests;

/// <summary>
/// The JSON the desktop client posts to redeem a pairing code, and what it reads back.
/// </summary>
/// <remarks>
/// <para>
/// Worth pinning because it is the one serialised path no ordinary run exercises: a paired app
/// never touches it again, so a break here surfaces only when somebody pairs a new machine —
/// which is exactly when nothing else about the app works yet either.
/// </para>
/// <para>
/// The property names matter literally. The client publishes them from a source-generated
/// context and the server binds them by name, so a rename on one side is a pairing that fails
/// with "not valid any more" and no hint as to why.
/// </para>
/// </remarks>
public class DeviceTokenExchangeTests
{
    [Fact]
    public void TheRequest_UsesTheNamesTheServerBinds()
    {
        var json = JsonSerializer.Serialize(
            new DeviceTokenRequest("the-code", "the-verifier", "http://127.0.0.1:49152/callback"),
            SoundboardJsonContext.Default.DeviceTokenRequest);

        Assert.Contains("\"code\":\"the-code\"", json);
        Assert.Contains("\"verifier\":\"the-verifier\"", json);
        Assert.Contains("\"redirectUri\":\"http://127.0.0.1:49152/callback\"", json);
    }

    [Fact]
    public void TheResponse_ReadsBack()
    {
        var response = JsonSerializer.Deserialize(
            """{"token":"ss_abc","userId":"1234567890123456789","userName":"alice"}""",
            SoundboardJsonContext.Default.DeviceTokenResponse);

        Assert.NotNull(response);
        Assert.Equal("ss_abc", response.Token);
        Assert.Equal("alice", response.UserName);
    }

    [Fact]
    public void TheUserId_StaysAString()
    {
        // A Discord snowflake exceeds what a double holds exactly, so a number here would come
        // back off by a digit or two and match nobody.
        var response = JsonSerializer.Deserialize(
            """{"token":"ss_abc","userId":"9007199254740993","userName":"alice"}""",
            SoundboardJsonContext.Default.DeviceTokenResponse);

        Assert.Equal("9007199254740993", response!.UserId);
    }

    [Fact]
    public void AnError_ReadsBack()
    {
        var error = JsonSerializer.Deserialize(
            """{"error":"That pairing code is not valid any more."}""",
            SoundboardJsonContext.Default.DeviceTokenError);

        Assert.Equal("That pairing code is not valid any more.", error!.Error);
    }

    [Fact]
    public void HubShapes_HaveGeneratedMetadata()
    {
        // The desktop client publishes trimmed, which disables reflection-based serialization
        // outright. A type that crosses the hub without being listed on the context throws the
        // first time it is used, so this asserts the context actually knows about each of them.
        Assert.NotNull(SoundboardJsonContext.Default.BotState);
        Assert.NotNull(SoundboardJsonContext.Default.PlayResult);
        Assert.NotNull(SoundboardJsonContext.Default.SoundSummary);
        Assert.NotNull(SoundboardJsonContext.Default.IReadOnlyListSoundSummary);
    }
}
