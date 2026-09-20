using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using SemperSounds.Contracts;
using SemperSounds.Desktop.Core;
using SemperSounds.Desktop.Interop;
using SemperSounds.Desktop.Services;
using SemperSounds.Desktop.Views;

namespace SemperSounds.Desktop.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>One row in the settings window: a clip and the key bound to it, if any.</summary>
public sealed class SoundRow(SoundSummary sound) : Observable
{
    private HotkeyChord _chord = HotkeyChord.None;

    public Guid Id { get; } = sound.Id;

    public string Name { get; } = sound.Name;

    public string Emoji { get; } = sound.EmojiText;

    public string Duration { get; } = $"{sound.DurationMs / 1000.0:0.0}s";

    public string Tags { get; } = string.Join(", ", sound.Tags);

    public HotkeyChord Chord
    {
        get => _chord;
        set
        {
            if (Set(ref _chord, value))
            {
                Raise(nameof(ChordText));
                Raise(nameof(HasChord));
            }
        }
    }

    public string ChordText => _chord.IsValid ? _chord.ToString() : "Click to assign";

    public bool HasChord => _chord.IsValid;

    public bool Matches(string term) =>
        term.Length == 0 ||
        Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        Tags.Contains(term, StringComparison.OrdinalIgnoreCase);
}

public sealed class MainWindowViewModel : Observable, IDisposable
{
    private readonly LocalStore _store = new();
    private readonly HttpClient _http = new();
    private readonly LowLevelKeyboardHook _hook = new();
    private readonly SoundboardConnection _connection;
    private readonly HotkeyRouter _router;
    private readonly HotkeyDispatcher _dispatcher;
    private readonly PairingFlow _pairing;

    private string? _token;
    private string _search = string.Empty;
    private string _serverUrl;
    private string _status = "Starting…";
    private string _pairingMessage = string.Empty;
    private bool _busy;

    public MainWindowViewModel()
    {
        Config = _store.LoadConfig();
        _token = _store.LoadToken();
        _serverUrl = Config.ServerUrl;

        _connection = new SoundboardConnection(() => _token);
        _router = new HotkeyRouter(Config);
        _dispatcher = new HotkeyDispatcher(_hook, _router, _connection, () => Config);
        _pairing = new PairingFlow(_http);

        _connection.Changed += OnConnectionChanged;
        _connection.LibraryChanged += () => Dispatcher.UIThread.Post(ReconcileBindings);
        _dispatcher.Pressed += OnPressed;
        _dispatcher.MuteChanged += () => Dispatcher.UIThread.Post(() => { Raise(nameof(IsMuted)); Raise(nameof(StatusText)); });
        _dispatcher.HookRecovered += () => Dispatcher.UIThread.Post(() =>
            Notify(new PressOutcome(false, "Hotkeys were restored", "Windows had dropped the keyboard hook.")));

        _hook.CallbackWasSlow += elapsed => Dispatcher.UIThread.Post(() =>
            Notify(new PressOutcome(false, "Hotkeys are running slowly",
                $"The keyboard hook took {elapsed.TotalMilliseconds:0}ms. Windows removes it above 300ms.")));
    }

    public DesktopConfig Config { get; private set; }

    public ToastWindow? Toast { get; set; }

    public ObservableCollection<SoundRow> Rows { get; } = [];

    public ObservableCollection<SoundRow> Visible { get; } = [];

    public event Action? ExitRequested;

    public event Action? ShowWindowRequested;

    public bool IsPaired => _token is not null;

    public bool IsMuted => _router.IsMuted;

    public bool IsBusy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }

    public string ServerUrl
    {
        get => _serverUrl;
        set => Set(ref _serverUrl, value);
    }

    public string PairingMessage
    {
        get => _pairingMessage;
        private set => Set(ref _pairingMessage, value);
    }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                ApplyFilter();
            }
        }
    }

    public string StatusText
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool AutoSummon
    {
        get => Config.AutoSummon;
        set { Config.AutoSummon = value; Raise(); Save(); }
    }

    public bool ShowOverlay
    {
        get => Config.ShowOverlay;
        set { Config.ShowOverlay = value; Raise(); Save(); }
    }

    public bool PlayAudioCue
    {
        get => Config.PlayAudioCue;
        set { Config.PlayAudioCue = value; Raise(); Save(); }
    }

    public bool StartMinimised
    {
        get => Config.StartMinimised;
        set { Config.StartMinimised = value; Raise(); Save(); }
    }

    /// <summary>Read through to the registry rather than from the config, every time.</summary>
    /// <remarks>
    /// Task Manager's startup tab can disable the entry without telling the app, and a
    /// checkbox that disagrees with what Windows will actually do is worse than none.
    /// </remarks>
    public bool RunAtStartup
    {
        get => StartupRegistration.IsEnabled;
        set { StartupRegistration.Set(value); Raise(); }
    }

    /// <summary>True when an elevated window is in front and the hook is therefore blind to it.</summary>
    public bool NeedsElevation => !Elevation.IsElevated && Elevation.ForegroundWindowIsOutOfReach();

    public HotkeyChord StopAllChord
    {
        get => Config.StopAllChord;
        set { Config.StopAllChord = value; Raise(); Rebind(); }
    }

    public HotkeyChord SummonChord
    {
        get => Config.SummonChord;
        set { Config.SummonChord = value; Raise(); Rebind(); }
    }

    public HotkeyChord MuteChord
    {
        get => Config.MuteChord;
        set { Config.MuteChord = value; Raise(); Rebind(); }
    }

    public async Task StartAsync()
    {
        _dispatcher.Start();

        if (IsPaired && !string.IsNullOrWhiteSpace(Config.ServerUrl))
        {
            await _connection.ConnectAsync(Config.ServerUrl);
        }
        else
        {
            StatusText = "Not paired yet.";
        }
    }

    public async Task PairAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        PairingMessage = "Finish signing in in your browser…";

        try
        {
            var result = await _pairing.PairAsync(ServerUrl.Trim());
            if (!result.IsSuccess)
            {
                PairingMessage = result.Error;
                return;
            }

            _token = result.Token;
            _store.SaveToken(result.Token);

            Config.ServerUrl = ServerUrl.Trim();
            Save();

            PairingMessage = $"Paired as {result.UserName}.";
            Raise(nameof(IsPaired));

            await _connection.ConnectAsync(Config.ServerUrl);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Forgets the credential on this machine.
    /// </summary>
    /// <remarks>
    /// Local only, and said so in the UI. The authority still exists on the server until it is
    /// revoked there, which is the page to use if the machine itself is the problem.
    /// </remarks>
    public void Unpair()
    {
        _store.ClearToken();
        _token = null;
        Rows.Clear();
        Visible.Clear();
        Raise(nameof(IsPaired));
        StatusText = "Not paired yet.";
        PairingMessage = "This device has been unpaired locally. Revoke it on the website to withdraw its access.";
    }

    public async Task SummonAsync()
    {
        var result = _connection.Bot.IsBotInYourChannel
            ? await LeaveAsync()
            : await _connection.JoinAsync();

        if (!result.IsSuccess)
        {
            Notify(new PressOutcome(false, "Could not move the bot", result.Error));
        }
    }

    private async Task<PlayResult> LeaveAsync()
    {
        await _connection.LeaveAsync();
        return PlayResult.Ok;
    }

    public void RelaunchAsAdministrator()
    {
        if (Elevation.RelaunchElevated())
        {
            ExitRequested?.Invoke();
        }
    }

    /// <summary>Waits for the next key the user presses, so it can be assigned.</summary>
    /// <remarks>
    /// Borrows the hook rather than using Avalonia's key events, which need window focus and
    /// cannot describe F13 to F24 — the range most worth binding, because no game claims it.
    /// </remarks>
    public Task<HotkeyChord> CaptureChordAsync(CancellationToken cancellationToken) =>
        _hook.CaptureNextAsync(cancellationToken);

    public void Show() => ShowWindowRequested?.Invoke();

    public void Exit() => ExitRequested?.Invoke();

    /// <summary>Assigns a chord to a row, clearing it from whatever held it before.</summary>
    /// <remarks>
    /// One key means one sound. Without the clearing step a chord could sit on two rows, and
    /// which of them fired would come down to list order.
    /// </remarks>
    public void Assign(SoundRow row, HotkeyChord chord)
    {
        if (chord.IsValid)
        {
            foreach (var other in Rows.Where(r => r != row && r.Chord == chord))
            {
                other.Chord = HotkeyChord.None;
            }

            if (Config.StopAllChord == chord) Config.StopAllChord = HotkeyChord.None;
            if (Config.SummonChord == chord) Config.SummonChord = HotkeyChord.None;
            if (Config.MuteChord == chord) Config.MuteChord = HotkeyChord.None;
        }

        row.Chord = chord;
        Rebind();
    }

    private void OnConnectionChanged() => Dispatcher.UIThread.Post(() =>
    {
        StatusText = Describe();
        Raise(nameof(NeedsElevation));

        if (_connection.Sounds.Count != 0 && Rows.Count == 0)
        {
            ReconcileBindings();
        }
    });

    private string Describe()
    {
        if (_router.IsMuted)
        {
            return "Hotkeys muted.";
        }

        return _connection.State switch
        {
            LinkState.Offline => IsPaired ? "Offline — cannot reach SemperSounds." : "Not paired yet.",
            LinkState.Connecting => "Connecting…",
            _ when !_connection.Bot.IsBotReady => "Connected. Waiting for Discord.",
            _ when _connection.Bot.IsBotInYourChannel => $"Ready — bot is in {_connection.Bot.BotChannelName}.",
            _ when _connection.Bot.IsBotConnected => $"Bot is in {_connection.Bot.BotChannelName}, you are not.",
            _ when _connection.Bot.AreYouInVoice => "Bot is idle. A key press will summon it.",
            _ => "Join a voice channel to play anything.",
        };
    }

    /// <summary>
    /// Rebuilds the rows from the server's library, keeping the chords already assigned.
    /// </summary>
    /// <remarks>
    /// A binding whose sound has been deleted is dropped here rather than left to fail at press
    /// time — otherwise the key simply stops working and nothing says why.
    /// </remarks>
    private void ReconcileBindings()
    {
        var assigned = Config.Bindings.ToDictionary(b => b.SoundId, b => b.Chord);

        Rows.Clear();
        foreach (var sound in _connection.Sounds.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = new SoundRow(sound);
            if (assigned.TryGetValue(sound.Id, out var chord))
            {
                row.Chord = chord;
            }

            Rows.Add(row);
        }

        ApplyFilter();
        Rebind();
    }

    private void ApplyFilter()
    {
        Visible.Clear();
        foreach (var row in Rows.Where(r => r.Matches(_search.Trim())))
        {
            Visible.Add(row);
        }
    }

    private void Rebind()
    {
        Config.Bindings = [.. Rows.Where(r => r.Chord.IsValid)
            .Select(r => new HotkeyBinding { SoundId = r.Id, Chord = r.Chord, CachedName = r.Name })];

        _dispatcher.Rebind(Config);
        Save();
    }

    private void Save() => _store.SaveConfig(Config);

    private void OnPressed(PressOutcome outcome) => Dispatcher.UIThread.Post(() => Notify(outcome));

    private void Notify(PressOutcome outcome)
    {
        StatusText = outcome.Detail.Length > 0 ? $"{outcome.Title} — {outcome.Detail}" : outcome.Title;

        if (Config.ShowOverlay)
        {
            Toast?.Flash(outcome);
        }
    }

    public void Dispose()
    {
        _dispatcher.Dispose();
        _hook.Dispose();
        _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _http.Dispose();
    }
}
