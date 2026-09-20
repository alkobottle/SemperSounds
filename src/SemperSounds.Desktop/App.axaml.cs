using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using SemperSounds.Desktop.Services;
using SemperSounds.Desktop.ViewModels;
using SemperSounds.Desktop.Views;

namespace SemperSounds.Desktop;

public partial class App : Application
{
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

        _instance = new SingleInstance();
        if (!_instance.IsFirstInstance)
        {
            // Bring the copy that is already running forward, then leave. Without this, running
            // the app again while it sits in the tray appears to do nothing at all.
            _instance.SignalExistingInstance();
            _instance.Dispose();
            desktop.Shutdown();
            return;
        }

        // Without this the first close of the settings window ends the process, taking the
        // keyboard hook with it — the app would work exactly once per launch.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

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

        _instance.ShowRequested += () => Dispatcher.UIThread.Post(ShowWindow);
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
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SemperSounds.Desktop/Assets/tray.ico"))),
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
