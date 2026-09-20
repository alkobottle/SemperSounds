using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private Bitmap? _emojiImage;
    private bool _isPreviewing;

    public Guid Id { get; } = sound.Id;

    public string Name { get; } = sound.Name;

    public string Emoji { get; } = sound.EmojiText;

    /// <summary>Null for a standard emoji, which is a character the font already draws.</summary>
    public string? EmojiImageUrl { get; } = sound.EmojiImageUrl;

    /// <summary>The fetched picture, once it arrives. Null until then, and null if it never does.</summary>
    public Bitmap? EmojiImage
    {
        get => _emojiImage;
        private set
        {
            if (Set(ref _emojiImage, value))
            {
                Raise(nameof(HasEmojiImage));
                Raise(nameof(ShowEmojiText));
            }
        }
    }

    public bool HasEmojiImage => _emojiImage is not null;

    /// <summary>Show the text form until a picture replaces it, and for ever if none is coming.</summary>
    public bool ShowEmojiText => _emojiImage is null;

    /// <summary>Fetches the custom emoji, if this sound has one. Never throws.</summary>
    public async Task LoadEmojiAsync(EmojiImages images)
    {
        if (EmojiImageUrl is null)
        {
            return;
        }

        var bitmap = await images.GetAsync(EmojiImageUrl);
        if (bitmap is not null)
        {
            EmojiImage = bitmap;
        }
    }

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
                Raise(nameof(ChordBrush));
            }
        }
    }

    public string ChordText => _chord.IsValid ? _chord.ToString() : "Click to assign";

    /// <summary>Blurple once a key is on it, muted while the row is only an invitation.</summary>
    public IBrush ChordBrush => _chord.IsValid ? DiscordPalette.Blurple : DiscordPalette.Muted;

    public bool HasChord => _chord.IsValid;

    /// <summary>True while this clip is the one previewing locally.</summary>
    public bool IsPreviewing
    {
        get => _isPreviewing;
        set
        {
            if (Set(ref _isPreviewing, value))
            {
                Raise(nameof(PreviewIcon));
            }
        }
    }

    /// <summary>The same button stops what it started, so it has to say which it will do.</summary>
    public string PreviewIcon => _isPreviewing ? Glyphs.Stop : Glyphs.Headphones;

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
    private readonly EmojiImages _emoji;
    private readonly SoundPreview _preview;

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
        _emoji = new EmojiImages(_http);
        _preview = new SoundPreview(_http, () => _token);
        _preview.Changed += () => Dispatcher.UIThread.Post(RefreshPreviewState);

        _connection.Changed += OnConnectionChanged;
        _connection.LibraryChanged += () => Dispatcher.UIThread.Post(ReconcileBindings);
        _dispatcher.Pressed += OnPressed;
        _dispatcher.MuteChanged += () => Dispatcher.UIThread.Post(() => { Raise(nameof(IsMuted)); Raise(nameof(StatusText)); Raise(nameof(StatusBrush)); });
        _dispatcher.HookRecovered += () => Dispatcher.UIThread.Post(() =>
            Notify(new PressOutcome(false, "Hotkeys were restored", "Windows had dropped the keyboard hook.")));

        _hook.CallbackWasSlow += elapsed => Dispatcher.UIThread.Post(() =>
            Notify(new PressOutcome(false, "Hotkeys are running slowly",
                $"The keyboard hook took {elapsed.TotalMilliseconds:0}ms. Windows removes it above 300ms.")));
    }

    public DesktopConfig Config { get; private set; }

    public ToastWindow? Toast { get; set; }

    public ObservableCollection<SoundRow> Rows { get; } = [];

    /// <summary>
    /// Sounds with a key on them, kept in their own list above the rest.
    /// </summary>
    /// <remarks>
    /// A board of two hundred clips has perhaps six bindings in it, and hunting for them
    /// alphabetically among the rest is the whole reason this is separate. Deliberately not
    /// filtered by the search box: searching is for finding something new to bind, and having
    /// your existing bindings disappear while you do it helps nobody.
    /// </remarks>
    public ObservableCollection<SoundRow> Assigned { get; } = [];

    public ObservableCollection<SoundRow> Unassigned { get; } = [];

    public bool HasAssigned => Assigned.Count > 0;

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
        catch (Exception ex)
        {
            // Deliberately catching everything. Every caller of this is an `async void` event
            // handler, where an escaping exception is not an error dialog — it is the whole
            // tray app vanishing mid-click, which is exactly how a malformed listener prefix
            // presented itself. Pairing failing is worth a sentence, never a crash.
            PairingMessage = $"Pairing failed: {ex.Message}";
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
        Assigned.Clear();
        Unassigned.Clear();
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

    /// <summary>
    /// Auditions a clip through this machine, not through the bot.
    /// </summary>
    /// <remarks>
    /// Hunting for the right clip should not fire every candidate into a channel full of
    /// people, which is exactly what previewing through the bot would mean.
    /// </remarks>
    public async Task PreviewAsync(SoundRow row)
    {
        var error = await _preview.PlayAsync(row.Id, Config.ServerUrl);
        if (error is not null)
        {
            StatusText = error;
        }
    }

    /// <summary>Plays a clip into the channel, exactly as pressing its key would.</summary>
    public async Task PlayNowAsync(SoundRow row)
    {
        var result = await _connection.PlayAsync(row.Id, Config.AutoSummon);

        StatusText = result.IsSuccess ? row.Name : $"{row.Name} — {result.Error}";
    }

    /// <summary>Keeps each row's preview button in step with what is actually sounding.</summary>
    private void RefreshPreviewState()
    {
        foreach (var row in Rows)
        {
            row.IsPreviewing = row.Id == _preview.Playing;
        }
    }

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

        // Re-partitioned here rather than inside Rebind, because a row that has just gained or
        // lost a key has to move between the two lists.
        ApplyFilter();
    }

    private void OnConnectionChanged() => Dispatcher.UIThread.Post(() =>
    {
        StatusText = Describe();

        // Raised by hand: the dot is computed from connection state rather than from
        // StatusText, so nothing else would tell the binding it had changed.
        Raise(nameof(StatusBrush));
        Raise(nameof(NeedsElevation));

        if (_connection.Sounds.Count != 0 && Rows.Count == 0)
        {
            ReconcileBindings();
        }
    });

    /// <summary>
    /// The status dot, using the same colours Discord uses for presence.
    /// </summary>
    /// <remarks>
    /// Green only when a key press would actually play something. Yellow is the honest middle:
    /// the link is up but something still stands in the way, and the sentence beside it says
    /// what. Grey means the app is deliberately not listening.
    /// </remarks>
    public IBrush StatusBrush => _router.IsMuted
        ? DiscordPalette.Muted
        : _connection.State switch
        {
            LinkState.Offline => DiscordPalette.Red,
            LinkState.Connecting => DiscordPalette.Yellow,
            _ when !_connection.Bot.IsBotReady => DiscordPalette.Yellow,
            _ when _connection.Bot.IsBotInYourChannel => DiscordPalette.Green,
            _ when _connection.Bot.AreYouInVoice => DiscordPalette.Yellow,
            _ => DiscordPalette.Muted,
        };

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
            _ = row.LoadEmojiAsync(_emoji);
        }

        ApplyFilter();
        Rebind();
    }

    private void ApplyFilter()
    {
        var term = _search.Trim();

        Assigned.Clear();
        Unassigned.Clear();

        foreach (var row in Rows)
        {
            if (row.Chord.IsValid)
            {
                Assigned.Add(row);
            }
            else if (row.Matches(term))
            {
                Unassigned.Add(row);
            }
        }

        Raise(nameof(HasAssigned));
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
        _preview.Dispose();
        _dispatcher.Dispose();
        _hook.Dispose();
        _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _http.Dispose();
    }
}
