using SemperSounds.Contracts;
using SemperSounds.Desktop.Core;
using SemperSounds.Desktop.Interop;

namespace SemperSounds.Desktop.Services;

/// <summary>
/// Joins the keyboard hook to the hub: reads presses, asks the router what they mean, and does it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs on a consumer task rather than in the hook callback, which is the
/// arrangement that keeps the callback inside Windows' timeout. The hook's only job is to
/// recognise a watched chord and write it to a channel.
/// </para>
/// <para>
/// A watchdog runs alongside, because Windows drops a low-level hook whose callback overran and
/// says nothing. Left unchecked, the app keeps sitting in the tray looking healthy while no key
/// works — so the hook is put back, and the fact that it happened is surfaced.
/// </para>
/// </remarks>
public sealed class HotkeyDispatcher(
    LowLevelKeyboardHook hook,
    HotkeyRouter router,
    SoundboardConnection connection,
    Func<DesktopConfig> config) : IDisposable
{
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _stopping = new();

    /// <summary>Raised after every press that produced a result worth showing.</summary>
    public event Action<PressOutcome>? Pressed;

    /// <summary>Raised when the hook had to be reinstalled, so the user can be told why keys went quiet.</summary>
    public event Action? HookRecovered;

    /// <summary>Raised when mute is toggled, so the tray icon can follow it.</summary>
    public event Action? MuteChanged;

    public void Start()
    {
        hook.Watch(router.WatchedChords);
        hook.Start();

        _ = Task.Run(ConsumeAsync, CancellationToken.None);
        _ = Task.Run(WatchdogAsync, CancellationToken.None);
    }

    /// <summary>Applies a changed configuration to both the router and the hook's watch set.</summary>
    public void Rebind(DesktopConfig updated)
    {
        router.Update(updated);
        hook.Watch(router.WatchedChords);
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var chord in hook.Presses.ReadAllAsync(_stopping.Token))
            {
                try
                {
                    await HandleAsync(chord);
                }
                catch (Exception ex)
                {
                    // One bad press must not end the loop; ending it would silently stop every
                    // subsequent key.
                    Pressed?.Invoke(new PressOutcome(false, "Something went wrong", ex.Message));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task HandleAsync(HotkeyChord chord)
    {
        var action = router.Route(chord);
        var settings = config();

        switch (action.Kind)
        {
            case HotkeyActionKind.None:
                return;

            case HotkeyActionKind.ToggleMute:
                MuteChanged?.Invoke();
                Report(settings, new PressOutcome(true, router.IsMuted ? "Hotkeys muted" : "Hotkeys active", string.Empty));
                return;

            case HotkeyActionKind.StopAll:
                await connection.StopAllAsync();
                Report(settings, new PressOutcome(true, "Stopped", string.Empty));
                return;

            case HotkeyActionKind.ToggleSummon:
                await ToggleSummonAsync(settings);
                return;

            case HotkeyActionKind.Play:
                await PlayAsync(settings, action);
                return;
        }
    }

    private async Task ToggleSummonAsync(DesktopConfig settings)
    {
        if (connection.Bot.IsBotInYourChannel)
        {
            await connection.LeaveAsync();
            Report(settings, new PressOutcome(true, "Bot dismissed", string.Empty));
            return;
        }

        var result = await connection.JoinAsync();
        Report(settings, result.IsSuccess
            ? new PressOutcome(true, "Bot summoned", string.Empty)
            : new PressOutcome(false, "Could not summon the bot", result.Error));
    }

    private async Task PlayAsync(DesktopConfig settings, HotkeyAction action)
    {
        var result = await connection.PlayAsync(action.SoundId, settings.AutoSummon);

        if (result.IsSuccess)
        {
            Report(settings, new PressOutcome(true, action.SoundName, string.Empty));
            return;
        }

        // A clip that is still playing says so itself, audibly, in the channel. Announcing it
        // as well would put a cue or a popup on every press of a held key, which is the noise
        // the refusal exists to prevent.
        if (!AutoSummonPolicy.ShouldReport(result.Failure))
        {
            return;
        }

        var title = result.Failure == PlayFailure.Missing
            ? $"\"{action.SoundName}\" is no longer on the board"
            : action.SoundName;

        Report(settings, new PressOutcome(false, title, result.Error));
    }

    private void Report(DesktopConfig settings, PressOutcome outcome)
    {
        // Only failures get a cue. A successful press announces itself perfectly well by the
        // clip playing, and a chirp on top of every one of those gets old within a session.
        if (settings.PlayAudioCue && !outcome.IsSuccess)
        {
            AudioCue.Play(success: false);
        }

        // The overlay decides for itself what to show; it is the subscriber's setting to read.
        Pressed?.Invoke(outcome);
    }

    private async Task WatchdogAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                await Task.Delay(WatchdogInterval, _stopping.Token);

                if (hook.Reinstall())
                {
                    hook.Watch(router.WatchedChords);
                    HookRecovered?.Invoke();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
