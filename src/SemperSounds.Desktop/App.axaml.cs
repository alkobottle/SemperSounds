using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using SemperSounds.Desktop.Services;
using SukiUI;
using SukiUI.Models;
using SemperSounds.Desktop.ViewModels;
using SemperSounds.Desktop.Views;

namespace SemperSounds.Desktop;

public partial class App : Application
{
    /// <summary>
    /// Set by <see cref="Program"/> before Avalonia starts. Non-null by the time the lifetime
    /// runs, because a second copy never gets this far.
    /// </summary>
    public SingleInstance? Instance { get; set; }

    private SingleInstance? _instance;
    private TrayIcon? _tray;
    private MainWindowViewModel? _viewModel;
    private MainWindow? _window;
    private ToastWindow? _toast;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }


        // Without this the first close of the settings window ends the process, taking the
        // keyboard hook with it — the app would work exactly once per launch.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ApplyDiscordTheme();

        _viewModel = new MainWindowViewModel();
        _viewModel.ExitRequested += () => Dispatcher.UIThread.Post(() => desktop.Shutdown());
        _viewModel.ShowWindowRequested += () => Dispatcher.UIThread.Post(ShowWindow);

        // Built once, at startup, and only ever shown and hidden afterwards. Its
        // no-activate styles have to be on the handle before it is first displayed, and a
        // window constructed per toast would be a frame late every time — long enough to pull
        // a fullscreen game out of focus, which is worse than showing nothing.
        _toast = new ToastWindow();
        _toast.Prepare();
        _viewModel.Toast = _toast;

        _instance = Instance;
        _instance!.ShowRequested += () => Dispatcher.UIThread.Post(ShowWindow);
        _instance.ListenForOtherInstances();

        desktop.Exit += (_, _) =>
        {
            _viewModel?.Dispose();
            _instance?.Dispose();
        };

        CreateTrayIcon(desktop);

        _ = _viewModel.StartAsync();

        if (!_viewModel.Config.StartMinimised || !_viewModel.IsPaired)
        {
            // An unpaired app has nothing useful to do in the tray, so it shows itself.
            ShowWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Puts SukiUI into dark mode and gives it Discord's brand colours.
    /// </summary>
    /// <remarks>
    /// Done in code because SukiUI owns its own base theme: the Application's
    /// <c>RequestedThemeVariant</c> does not drive it, and leaving it to decide produced a
    /// light chrome under surfaces written for a dark one — white text on pale grey, unreadable
    /// throughout. Setting it explicitly is the only way to know which variant is in force.
    /// </remarks>
    private void ApplyDiscordTheme()
    {
        var theme = SukiTheme.GetInstance(this);

        theme.ChangeBaseTheme(ThemeVariant.Dark);
        theme.ChangeColorTheme(new SukiColorTheme(
            "Discord",
            Color.Parse("#5865F2"),   // blurple, for primary actions
            Color.Parse("#23A559"))); // the online green, as the accent
    }

    /// <summary>
    /// Builds the tray icon in code rather than XAML, so its tooltip can follow the live
    /// status — which is the only thing telling the user whether a key press would work while
    /// no window is open.
    /// </summary>
    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var open = new NativeMenuItem("Open SemperSounds");
        open.Click += (_, _) => ShowWindow();

        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => desktop.Shutdown();

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SemperSounds.Desktop/Assets/app.ico"))),
            ToolTipText = "SemperSounds",
            IsVisible = true,
            Menu = [open, new NativeMenuItemSeparator(), exit],
        };

        _tray.Clicked += (_, _) => ShowWindow();

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.StatusText))
                {
                    _tray.ToolTipText = $"SemperSounds — {_viewModel.StatusText}";
                }
            };
        }

        TrayIcon.SetIcons(this, [_tray]);
    }

    private void ShowWindow()
    {
        if (_viewModel is null)
        {
            return;
        }

        // Recreated rather than kept hidden: a settings window nobody is looking at should not
        // hold a render surface for days.
        if (_window is null)
        {
            _window = new MainWindow { DataContext = _viewModel };
            _window.Closed += (_, _) => _window = null;
        }

        _window.Show();
        _window.Activate();
    }

    /// <summary>Handlers for the tray menu, wired from App.axaml's NativeMenu.</summary>
    public void OnTrayOpen(object? sender, EventArgs e) => ShowWindow();

    public void OnTrayExit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
