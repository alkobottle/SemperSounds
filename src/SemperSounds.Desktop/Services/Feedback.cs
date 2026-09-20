using System.Runtime.InteropServices;

namespace SemperSounds.Desktop.Services;

/// <summary>What a press turned out to mean, for whatever is showing feedback.</summary>
public readonly record struct PressOutcome(bool IsSuccess, string Title, string Detail);

/// <summary>
/// A short sound on this machine, to confirm a press the user cannot see the result of.
/// </summary>
/// <remarks>
/// <para>
/// The reliable half of press feedback. The overlay window cannot composite over a game running
/// in exclusive fullscreen, and a window that takes focus would minimise one — a system sound
/// has neither problem, because it never draws anything and never touches focus.
/// </para>
/// <para>
/// <c>MessageBeep</c> rather than a bundled audio file: it is asynchronous, it uses whatever
/// output device the user has chosen, it needs no package, and it respects the fact that the
/// user may have turned system sounds off, which is a preference worth honouring.
/// </para>
/// </remarks>
public static partial class AudioCue
{
    private const uint Ok = 0x00000000;
    private const uint Exclamation = 0x00000030;

    public static void Play(bool success) => MessageBeep(success ? Ok : Exclamation);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint uType);
}

/// <summary>
/// Keeps one copy of the app running, and lets a second one bring the first to the front.
/// </summary>
/// <remarks>
/// A mutex alone only <em>detects</em> a second instance; it has no way to tell the first one
/// anything. Without the event as well, launching the app again while it sits in the tray does
/// nothing at all — which reads as the app being broken. So the second instance signals and
/// exits, and the first shows its window.
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    // Local\ rather than Global\: one instance per logged-in user is what is wanted, and a
    // global name would make two people on one machine fight over it.
    private const string MutexName = @"Local\SemperSounds.Desktop.Instance";
    private const string EventName = @"Local\SemperSounds.Desktop.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequested;
    private readonly CancellationTokenSource _listening = new();

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
        IsFirstInstance = isFirst;
        _showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
    }

    public bool IsFirstInstance { get; }

    /// <summary>Raised on a background thread when another copy asks to be brought forward.</summary>
    public event Action? ShowRequested;

    /// <summary>Tells the running instance to show itself.</summary>
    public void SignalExistingInstance() => _showRequested.Set();

    public void ListenForOtherInstances() => _ = Task.Run(() =>
    {
        var waits = new[] { _showRequested, _listening.Token.WaitHandle };

        while (!_listening.IsCancellationRequested)
        {
            if (WaitHandle.WaitAny(waits) == 0)
            {
                ShowRequested?.Invoke();
            }
        }
    }, CancellationToken.None);

    public void Dispose()
    {
        _listening.Cancel();

        try
        {
            if (IsFirstInstance)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (Exception ex) when (ex is AbandonedMutexException or ApplicationException)
        {
            // Releasing a mutex this process no longer owns is a tear-down detail, not a fault
            // worth surfacing on the way out.
        }

        _mutex.Dispose();
        _showRequested.Dispose();
        _listening.Dispose();
    }
}
