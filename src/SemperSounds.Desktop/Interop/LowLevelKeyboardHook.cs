using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SemperSounds.Desktop.Core;

namespace SemperSounds.Desktop.Interop;

/// <summary>
/// Watches the keyboard for bound chords without taking the keys away from anything else.
/// </summary>
/// <remarks>
/// <para>
/// A low-level hook rather than <c>RegisterHotKey</c>, and that choice is the whole design.
/// <c>RegisterHotKey</c> <em>consumes</em> the key system-wide: binding F1 would mean F1 stops
/// opening the game's help, stops working in the browser, stops existing. A hook sees the key
/// and then passes it on, so a bound key keeps doing whatever it always did.
/// </para>
/// <para>
/// It costs three things, each handled below. The callback runs on every keystroke on the
/// machine and must return inside Windows' <c>LowLevelHooksTimeout</c> — 300 ms by default —
/// or Windows silently unhooks it and every binding stops working with nothing thrown and
/// nothing logged. The hook needs a thread with a real message loop. And, by UIPI, a
/// non-elevated hook never sees keys destined for an elevated window, which is why
/// <see cref="Elevation"/> exists.
/// </para>
/// </remarks>
public sealed partial class LowLevelKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int HcAction = 0;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int WmQuit = 0x0012;

    /// <summary>
    /// How long the callback may take before Windows is entitled to drop the hook. Measured
    /// and reported rather than assumed, because exceeding it fails silently.
    /// </summary>
    private static readonly TimeSpan CallbackBudget = TimeSpan.FromMilliseconds(100);

    private readonly Channel<HotkeyChord> _presses = Channel.CreateUnbounded<HotkeyChord>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    // Held in a field: a delegate passed to SetWindowsHookEx and then collected leaves Windows
    // calling into freed memory, and the crash lands nowhere near here.
    private readonly LowLevelKeyboardProc _callback;
    private readonly Lock _gate = new();

    private Thread? _thread;
    private nint _hook;
    private uint _threadId;
    private HashSet<HotkeyChord> _watched = [];
    private HotkeyModifiers _modifiers;
    private int _lastKeyDown;
    private TaskCompletionSource<HotkeyChord>? _capture;

    public LowLevelKeyboardHook() => _callback = OnKeyboardEvent;

    /// <summary>Chords seen since the last read. Consumed by a task, never by the callback.</summary>
    public ChannelReader<HotkeyChord> Presses => _presses.Reader;

    /// <summary>False once Windows has dropped the hook, so the tray can say so.</summary>
    public bool IsInstalled => _hook != 0;

    /// <summary>Raised when the callback came uncomfortably close to the timeout.</summary>
    public event Action<TimeSpan>? CallbackWasSlow;

    /// <summary>
    /// Replaces the set of chords worth reacting to.
    /// </summary>
    /// <remarks>
    /// A set, swapped wholesale, because the callback's only job is a hash lookup — walking a
    /// list of bindings on every keystroke is what eats the budget.
    /// </remarks>
    public void Watch(IReadOnlySet<HotkeyChord> chords)
    {
        lock (_gate)
        {
            _watched = [.. chords];
        }
    }

    /// <summary>
    /// Installs the hook on a thread of its own.
    /// </summary>
    /// <remarks>
    /// Its own thread with its own message loop, rather than the UI thread, because the app can
    /// start straight to the tray with no window in existence — and because a UI thread busy
    /// laying out a list is a UI thread not answering the hook inside its budget.
    /// </remarks>
    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        var ready = new ManualResetEventSlim();

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();

            // No module handle: a low-level hook is called back in the installing thread, so
            // unlike other global hooks there is nothing to inject and no DLL to name.
            _hook = SetWindowsHookEx(WhKeyboardLl, _callback, 0, 0);
            ready.Set();

            if (_hook == 0)
            {
                return;
            }

            // GetMessage rather than a bare sleep: Windows delivers low-level hook callbacks
            // by way of the installing thread's message queue, so a thread that never pumps
            // never receives a single key.
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        })
        {
            IsBackground = true,
            Name = "SemperSounds hotkeys",
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    /// <summary>
    /// Puts the hook back if Windows has dropped it. True when it had to act.
    /// </summary>
    /// <remarks>
    /// Windows drops a low-level hook whose callback overran, and tells nobody. Without a
    /// watchdog calling this, the only symptom is that hotkeys stop working until the app is
    /// restarted — which looks exactly like the app having crashed, except it is still sitting
    /// in the tray.
    /// </remarks>
    public bool Reinstall()
    {
        if (_hook != 0)
        {
            return false;
        }

        Stop();
        Start();
        return true;
    }

    private nint OnKeyboardEvent(int code, nint wParam, nint lParam)
    {
        // Always call the next hook, on every path including the exceptional one. Returning
        // anything else here is what would swallow the key.
        if (code != HcAction)
        {
            return CallNextHookEx(0, code, wParam, lParam);
        }

        var started = Stopwatch.GetTimestamp();

        try
        {
            var message = (int)wParam;
            var key = Marshal.ReadInt32(lParam);

            if (message is WmKeyDown or WmSysKeyDown)
            {
                TrackModifier(key, down: true);

                // Auto-repeat arrives as a stream of key-downs with no key-up between them.
                // Leaning on a bound key would otherwise be a clip per repeat.
                if (key != _lastKeyDown)
                {
                    _lastKeyDown = key;
                    Match(key);
                }
            }
            else if (message is WmKeyUp or WmSysKeyUp)
            {
                TrackModifier(key, down: false);

                if (key == _lastKeyDown)
                {
                    _lastKeyDown = 0;
                }
            }
        }
        catch
        {
            // Nothing may escape into Windows' hook dispatch, and nothing here is worth
            // dropping a keystroke over.
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        if (elapsed > CallbackBudget)
        {
            CallbackWasSlow?.Invoke(elapsed);
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>
    /// Waits for the next key the user presses, whatever it is.
    /// </summary>
    /// <remarks>
    /// Assigning a binding cannot go through Avalonia's own key events: the window has to have
    /// focus for those, and they do not describe the keys most worth binding here — F13 to F24
    /// on a macro keyboard, which is the range no game has claimed. The hook already sees
    /// every key, so capture borrows it. While armed it reports unwatched chords too, and the
    /// captured press is swallowed as a press so that assigning a key does not also fire it.
    /// </remarks>
    public Task<HotkeyChord> CaptureNextAsync(CancellationToken cancellationToken)
    {
        var capture = new TaskCompletionSource<HotkeyChord>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _capture?.TrySetCanceled();
            _capture = capture;
        }

        cancellationToken.Register(() =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_capture, capture))
                {
                    _capture = null;
                }
            }

            capture.TrySetCanceled();
        });

        return capture.Task;
    }

    private void Match(int key)
    {
        HotkeyModifiers modifiers;
        TaskCompletionSource<HotkeyChord>? capture;

        lock (_gate)
        {
            modifiers = _modifiers;
            capture = _capture;

            if (capture is not null)
            {
                // Assigning Ctrl+F1 means pressing Ctrl first. Consuming that would hand back
                // a chord whose key is a modifier - invalid, discarded by the caller, and the
                // attempt is over before the user has finished making it. So capture waits for
                // a key that can actually carry a binding, and the modifiers ride along in the
                // state tracked above.
                if (HotkeyChord.IsModifier(key))
                {
                    return;
                }

                _capture = null;
            }
            else if (!_watched.Contains(new HotkeyChord(key, modifiers)))
            {
                return;
            }
        }

        var chord = new HotkeyChord(key, modifiers);

        if (capture is not null)
        {
            // Completed rather than dispatched: the key that was just assigned must not also
            // play whatever it was assigned to.
            capture.TrySetResult(chord);
            return;
        }

        // Writing to an unbounded channel does not block, which is what keeps this inside the
        // budget: every decision about what the press means happens on the reader's thread.
        _presses.Writer.TryWrite(chord);
    }

    /// <summary>
    /// Tracks modifier state from the hook's own stream rather than asking Windows.
    /// </summary>
    /// <remarks>
    /// <c>GetAsyncKeyState</c> would answer for <em>now</em>, which is a different moment from
    /// the one that produced this key — so a fast Ctrl release between the press and the
    /// callback would turn "Ctrl+F1" into "F1". What the hook saw is the only self-consistent
    /// account of what the user did.
    /// </remarks>
    private void TrackModifier(int key, bool down)
    {
        var flag = key switch
        {
            0x10 or 0xA0 or 0xA1 => HotkeyModifiers.Shift,
            0x11 or 0xA2 or 0xA3 => HotkeyModifiers.Control,
            0x12 or 0xA4 or 0xA5 => HotkeyModifiers.Alt,
            0x5B or 0x5C => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };

        if (flag == HotkeyModifiers.None)
        {
            return;
        }

        lock (_gate)
        {
            _modifiers = down ? _modifiers | flag : _modifiers & ~flag;
        }
    }

    private void Stop()
    {
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }

        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, 0, 0);
            _threadId = 0;
        }

        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _presses.Writer.TryComplete();
    }

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    private static nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId) =>
        SetWindowsHookExW(idHook, lpfn, hMod, dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Msg lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(ref Msg lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
