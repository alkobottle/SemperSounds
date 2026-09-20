using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SemperSounds.Desktop.Services;

namespace SemperSounds.Desktop.Views;

/// <summary>
/// A small panel near the corner of the screen, for feedback the user cannot otherwise see.
/// </summary>
/// <remarks>
/// <para>
/// Two things make this delicate, and both are handled in <see cref="Prepare"/>. A topmost
/// window shown from a hotkey can take focus, and taking focus from a fullscreen game minimises
/// it — which is a far worse outcome than the user simply not seeing why a clip did not play.
/// And a click landing on it would go to this window rather than the game underneath.
/// </para>
/// <para>
/// It also cannot draw over a game running in <em>exclusive</em> fullscreen; nothing can. That
/// is why the audio cue exists alongside it, and why both are off until the user has seen how
/// their own game behaves.
/// </para>
/// </remarks>
public partial class ToastWindow : Window
{
    private const int GwlExStyle = -20;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExLayered = 0x00080000;

    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(2.5);

    private readonly DispatcherTimer _hideAfter;

    public ToastWindow()
    {
        InitializeComponent();

        _hideAfter = new DispatcherTimer { Interval = VisibleFor };
        _hideAfter.Tick += (_, _) =>
        {
            _hideAfter.Stop();
            Hide();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Creates the native window and applies the styles that keep it out of the way, once.
    /// </summary>
    /// <remarks>
    /// Called at startup rather than on the first toast. The extended styles have to be on the
    /// handle before the window is ever displayed — setting them in a shown window's Opened
    /// handler is one frame too late, and one frame is enough to pull a game out of focus.
    /// </remarks>
    public void Prepare()
    {
        // Shown off-screen and hidden again: the handle does not exist until the window has
        // been realised, and there is nothing to restyle before then.
        Position = new PixelPoint(-10_000, -10_000);
        Show();
        ApplyClickThroughStyles();
        Hide();
    }

    private void ApplyClickThroughStyles()
    {
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle == 0)
        {
            return;
        }

        var style = GetWindowLongPtr(handle, GwlExStyle);
        SetWindowLongPtr(handle, GwlExStyle,
            style | (nint)(WsExNoActivate | WsExToolWindow | WsExTransparent | WsExLayered));
    }

    /// <summary>Shows one message, then hides itself again.</summary>
    public void Flash(PressOutcome outcome)
    {
        this.FindControl<TextBlock>("TitleText")!.Text = outcome.Title;

        var detail = this.FindControl<TextBlock>("DetailText")!;
        detail.Text = outcome.Detail;
        detail.IsVisible = outcome.Detail.Length > 0;

        if (Screens.Primary is { } screen)
        {
            // Bottom-right, inside the working area so it does not sit under the taskbar.
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            Position = new PixelPoint(
                area.X + area.Width - (int)(Width * scale) - 24,
                area.Y + area.Height - (int)(Height * scale) - 24);
        }

        Show();
        _hideAfter.Stop();
        _hideAfter.Start();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
}
