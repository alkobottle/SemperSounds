using Avalonia;

namespace SemperSounds.Desktop;

internal static class Program
{
    /// <remarks>
    /// STAThread because the app talks to shell APIs — opening the browser for pairing, and the
    /// tray icon itself — which expect a single-threaded apartment.
    /// </remarks>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
