using SemperSounds.Web.Services;

namespace SemperSounds.Tests;

public class GatewayReadinessTests
{
    [Fact]
    public void BeforeTheGatewayConnects_NothingIsReady()
    {
        Assert.False(new GatewayReadiness().IsReady);
    }

    [Fact]
    public void Ready_MakesTheGatewayUsable()
    {
        var readiness = new GatewayReadiness();

        Assert.True(readiness.MarkReady());
        Assert.True(readiness.IsReady);
    }

    [Fact]
    public void Disconnect_MakesTheGatewayUnusable()
    {
        var readiness = new GatewayReadiness();
        readiness.MarkReady();

        Assert.True(readiness.MarkDisconnected());
        Assert.False(readiness.IsReady);
    }

    [Fact]
    public void Resume_RestoresReadinessAfterADrop()
    {
        // Discord answers a reconnect with RESUMED far more often than READY, and NetCord
        // raises a different event for each. Treating only READY as "usable" leaves the bot
        // permanently refusing to join after the first transient drop, even though the
        // gateway is healthy and heartbeating.
        var readiness = new GatewayReadiness();
        readiness.MarkReady();
        readiness.MarkDisconnected();

        Assert.True(readiness.MarkResumed());
        Assert.True(readiness.IsReady);
    }

    [Fact]
    public void RepeatedDisconnects_ReportNoFurtherChange()
    {
        // The UI redraws on every reported change, so a flapping connection must not
        // announce a transition it did not make.
        var readiness = new GatewayReadiness();
        readiness.MarkReady();
        readiness.MarkDisconnected();

        Assert.False(readiness.MarkDisconnected());
        Assert.False(readiness.IsReady);
    }

    [Fact]
    public void ReadyRepeatedWithoutADrop_ReportsNoChange()
    {
        var readiness = new GatewayReadiness();
        readiness.MarkReady();

        Assert.False(readiness.MarkReady());
        Assert.True(readiness.IsReady);
    }
}
