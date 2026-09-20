using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SemperSounds.Desktop.Core;
using SemperSounds.Desktop.ViewModels;

namespace SemperSounds.Desktop.Views;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _capturing;

    public MainWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MainWindowViewModel? Model => DataContext as MainWindowViewModel;

    /// <remarks>
    /// Closing hides rather than exits. The app's whole job happens while no window is open, so
    /// the close button meaning "quit" would make it work exactly once per launch. Quitting is
    /// the tray menu's Exit.
    /// </remarks>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _capturing?.Cancel();
        base.OnClosing(e);
    }

    private async void OnCapture(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SoundRow row } button || Model is null)
        {
            return;
        }

        _capturing?.Cancel();
        _capturing = new CancellationTokenSource();
        _capturing.CancelAfter(TimeSpan.FromSeconds(5));

        button.Content = "Press a key…";

        try
        {
            var chord = await Model.CaptureChordAsync(_capturing.Token);
            if (chord.IsValid)
            {
                Model.Assign(row, chord);
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out, or the user started assigning a different row. Either way the binding
            // is left exactly as it was.
        }
        finally
        {
            // Restored from the row rather than from what the button showed before, so a
            // successful assignment displays the new chord and a cancelled one the old.
            button.Content = row.ChordText;
        }
    }

    private async void OnPreview(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SoundRow row } && Model is not null)
        {
            await Model.PreviewAsync(row);
        }
    }

    private async void OnPlay(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SoundRow row } && Model is not null)
        {
            await Model.PlayNowAsync(row);
        }
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SoundRow row } && Model is not null)
        {
            Model.Assign(row, HotkeyChord.None);
        }
    }

    private async void OnPair(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.PairAsync();
        }
    }

    private void OnUnpair(object? sender, RoutedEventArgs e) => Model?.Unpair();

    private async void OnSummon(object? sender, RoutedEventArgs e)
    {
        if (Model is not null)
        {
            await Model.SummonAsync();
        }
    }

    private void OnRelaunch(object? sender, RoutedEventArgs e) => Model?.RelaunchAsAdministrator();
}
