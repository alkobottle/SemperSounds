using Avalonia;
using SemperSounds.Desktop.Services;

namespace SemperSounds.Desktop;

internal static class Program
{
    /// <remarks>
    /// STAThread because the app talks to shell APIs — opening the browser for pairing, and the
    /// tray icon itself — which expect a single-threaded apartment.
    /// </remarks>
    [STAThread]
    public static void Main(string[] args)
    {
        // Registered before the app builds, so the first icon to render already has a provider.
        // This package is built against Avalonia 11 while the app is on 12, which is exactly
        // the kind of mismatch that fails at runtime rather than at build - the icons are
        // verified on screen, not assumed.
        // Checked before Avalonia is touched at all. Doing it from inside the lifetime meant
        // calling Shutdown() before the main loop had started, which left Avalonia to run a
        // loop on an already-dead dispatcher and exit with an unhandled exception instead of
        // simply going away.
        var instance = new SingleInstance();
        if (!instance.IsFirstInstance)
        {
            instance.SignalExistingInstance();
            instance.Dispose();
            return;
        }

        BuildAvaloniaApp()
            .AfterSetup(builder => ((App)builder.Instance!).Instance = instance)
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()

        .WithInterFont()
        .LogToTrace();
}
