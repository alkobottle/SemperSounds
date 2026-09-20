namespace SemperSounds.Web.Services;

public static class DesktopServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services behind the Windows companion app.
    /// </summary>
    /// <remarks>
    /// One method rather than three loose lines in <c>Program.cs</c>, because the pair below
    /// has to stay a pair. A singleton nobody resolves is never constructed, and
    /// <see cref="DesktopBroadcaster"/> only subscribes to events — so on its own it would
    /// simply never come into existence, and every tray icon would freeze on whatever it was
    /// told when it connected, with nothing logged and nothing thrown.
    /// </remarks>
    public static IServiceCollection AddDesktopCompanion(this IServiceCollection services)
    {
        services.AddSingleton<DesktopBroadcaster>();

        // Resolved, not constructed: AddHostedService<DesktopBroadcaster>() would build a
        // second instance, leaving the hub registering connections on one object while the
        // gateway events fired on another.
        services.AddHostedService(sp => sp.GetRequiredService<DesktopBroadcaster>());

        // Holds one-time pairing codes, which outlive the request that mints them and are
        // worth nothing after a restart.
        services.AddSingleton<DeviceCodeStore>();

        return services;
    }
}
