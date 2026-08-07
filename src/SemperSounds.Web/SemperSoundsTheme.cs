using MudBlazor;

namespace SemperSounds.Web;

/// <summary>
/// The app's MudBlazor theme. Fonts are a system stack rather than the Google-hosted Roboto
/// MudBlazor documents, so a self-hosted instance has no external dependency at render time.
/// </summary>
public static class SemperSoundsTheme
{
    private static readonly string[] FontStack =
    [
        "-apple-system", "BlinkMacSystemFont", "Segoe UI", "Roboto",
        "Helvetica Neue", "Arial", "sans-serif"
    ];

    public static MudTheme Instance { get; } = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#5865F2",       // Discord blurple
            Secondary = "#EB459E",
            AppbarBackground = "#1E1F22",
            Background = "#181A1D",
            Surface = "#232529",
            Error = "#ED4245",
            Success = "#23A55A",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = FontStack },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
        },
    };
}
