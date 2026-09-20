using Avalonia.Media;

namespace SemperSounds.Desktop.ViewModels;

/// <summary>
/// The handful of Discord colours the view models hand back as brushes.
/// </summary>
/// <remarks>
/// The same values are declared in <c>App.axaml</c>, where the markup reaches them by key.
/// These exist because a status dot has to pick its colour from live state, which a static
/// resource reference cannot do — and they are frozen, so one instance is shared rather than a
/// brush allocated on every status change.
/// </remarks>
public static class DiscordPalette
{
    public static IBrush Blurple { get; } = Frozen("#5865F2");

    public static IBrush Green { get; } = Frozen("#23A559");

    public static IBrush Yellow { get; } = Frozen("#F0B132");

    public static IBrush Red { get; } = Frozen("#DA373C");

    public static IBrush Muted { get; } = Frozen("#949BA4");

    private static IBrush Frozen(string hex) => new SolidColorBrush(Color.Parse(hex)).ToImmutable();
}
