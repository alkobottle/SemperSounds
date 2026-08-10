namespace SemperSounds.Web.Services;

/// <summary>
/// Tracks whether the Discord gateway is usable, as a plain state machine so the
/// transitions can be tested without a connection.
/// </summary>
public sealed class GatewayReadiness
{
    /// <summary>True while the gateway can serve joins and playback.</summary>
    public bool IsReady { get; private set; }

    /// <summary>The gateway identified afresh. Returns whether this changed anything.</summary>
    public bool MarkReady() => Set(true);

    /// <summary>The gateway resumed an existing session.</summary>
    public bool MarkResumed() => Set(true);

    /// <summary>The socket dropped.</summary>
    public bool MarkDisconnected() => Set(false);

    private bool Set(bool ready)
    {
        if (IsReady == ready)
        {
            return false;
        }

        IsReady = ready;
        return true;
    }
}
