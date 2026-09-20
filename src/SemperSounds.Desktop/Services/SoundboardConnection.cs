using Microsoft.AspNetCore.SignalR.Client;
using SemperSounds.Contracts;
using SemperSounds.Desktop.Core;

namespace SemperSounds.Desktop.Services;

public enum LinkState { Offline, Connecting, Online }

/// <summary>
/// The app's end of the hub.
/// </summary>
/// <remarks>
/// <para>
/// Everything a hotkey does goes through here, and nothing here decides anything: the server
/// refuses or allows, and <see cref="AutoSummonPolicy"/> decides what to do about a refusal.
/// </para>
/// <para>
/// State is re-read on every reconnect rather than trusted across the gap. A push missed while
/// the socket was down would otherwise leave the app believing the bot is still in the user's
/// channel, and the first press after a reconnect would fail for a reason the app had already
/// ruled out.
/// </para>
/// </remarks>
public sealed class SoundboardConnection : IAsyncDisposable
{
    private readonly Func<string?> _tokenProvider;
    private readonly RetriggerGuard _guard = new();
    private HubConnection? _hub;

    public SoundboardConnection(Func<string?> tokenProvider) => _tokenProvider = tokenProvider;

    public LinkState State { get; private set; } = LinkState.Offline;

    public BotState Bot { get; private set; } = BotState.Unknown;

    public IReadOnlyList<SoundSummary> Sounds { get; private set; } = [];

    public event Action? Changed;

    /// <summary>Raised when the server says the library changed, so bindings can be reconciled.</summary>
    public event Action? LibraryChanged;

    public async Task ConnectAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        await DisposeHubAsync();

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var server) || _tokenProvider() is null)
        {
            Set(LinkState.Offline);
            return;
        }

        Set(LinkState.Connecting);

        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server, DesktopHubMethods.Route), options =>
            {
                // Read per request rather than captured, so re-pairing takes effect without
                // rebuilding the connection.
                options.AccessTokenProvider = () => Task.FromResult(_tokenProvider());
            })
            .WithAutomaticReconnect()
            .Build();

        hub.On<BotState>(nameof(ISoundboardClient.StateChanged), state =>
        {
            Bot = state;
            Changed?.Invoke();
        });

        hub.On(nameof(ISoundboardClient.LibraryChanged), () =>
        {
            LibraryChanged?.Invoke();
            _ = RefreshSoundsAsync();
        });

        hub.Reconnecting += _ =>
        {
            Set(LinkState.Connecting);
            return Task.CompletedTask;
        };

        // Re-read rather than resume: a state push missed across the gap would leave the app
        // acting on an answer that is no longer true.
        hub.Reconnected += async _ =>
        {
            Set(LinkState.Online);
            await RefreshAsync();
        };

        hub.Closed += _ =>
        {
            Bot = BotState.Unknown;
            Set(LinkState.Offline);
            return Task.CompletedTask;
        };

        _hub = hub;

        try
        {
            await hub.StartAsync(cancellationToken);
            Set(LinkState.Online);
            await RefreshAsync();
        }
        catch (Exception)
        {
            // Includes a rejected token, which is ordinary rather than exceptional: the user
            // may have revoked this device from the website a moment ago.
            Set(LinkState.Offline);
        }
    }

    public async Task RefreshAsync()
    {
        if (_hub is not { State: HubConnectionState.Connected })
        {
            return;
        }

        try
        {
            Bot = await _hub.InvokeAsync<BotState>(DesktopHubMethods.GetState);
            await RefreshSoundsAsync();
        }
        catch (Exception)
        {
            Set(LinkState.Offline);
        }
    }

    private async Task RefreshSoundsAsync()
    {
        if (_hub is not { State: HubConnectionState.Connected })
        {
            return;
        }

        try
        {
            Sounds = await _hub.InvokeAsync<IReadOnlyList<SoundSummary>>(DesktopHubMethods.GetSounds);
            Changed?.Invoke();
        }
        catch (Exception)
        {
            Set(LinkState.Offline);
        }
    }

    /// <summary>
    /// Plays a clip, summoning the bot first if that is what stands in the way.
    /// </summary>
    /// <remarks>
    /// The retry is deliberately once and never a loop: if joining did not fix it, pressing the
    /// same key harder will not either, and a loop here is a loop of gateway traffic.
    /// </remarks>
    public async Task<PlayResult> PlayAsync(Guid soundId, bool autoSummon)
    {
        // Refused here rather than at the far end, so a hammered key costs nothing at all.
        // The duration comes from the library the client already holds; the server re-checks
        // against the mixer regardless, and it is the one that speaks for everybody.
        var duration = Sounds.FirstOrDefault(s => s.Id == soundId) is { } sound
            ? TimeSpan.FromMilliseconds(sound.DurationMs)
            : (TimeSpan?)null;

        if (!_guard.TryBegin(soundId, duration))
        {
            return PlayResult.Fail(PlayFailure.AlreadyPlaying, "That sound is still playing.");
        }

        var result = await InvokePlayAsync(DesktopHubMethods.Play, soundId);

        if (!AutoSummonPolicy.ShouldSummon(result.Failure, autoSummon))
        {
            if (!result.IsSuccess)
            {
                // It never started, so nothing should be waiting on it to finish.
                _guard.Clear(soundId);
            }

            return result;
        }

        var joined = await JoinAsync();
        if (!joined.IsSuccess)
        {
            _guard.Clear(soundId);
            return joined;
        }

        var retried = await InvokePlayAsync(DesktopHubMethods.Play, soundId);
        if (!retried.IsSuccess)
        {
            _guard.Clear(soundId);
        }

        return retried;
    }

    public Task<PlayResult> JoinAsync() => InvokePlayAsync(DesktopHubMethods.Join, null);

    public Task LeaveAsync() => InvokeAsync(DesktopHubMethods.Leave);

    public Task StopAllAsync()
    {
        // The clips are ending, so nothing is still playing and nothing should stay blocked.
        _guard.Reset();
        return InvokeAsync(DesktopHubMethods.StopAll);
    }

    private async Task<PlayResult> InvokePlayAsync(string method, Guid? soundId)
    {
        if (_hub is not { State: HubConnectionState.Connected })
        {
            return PlayResult.Fail(PlayFailure.Other, "Not connected to SemperSounds.");
        }

        try
        {
            return soundId is { } id
                ? await _hub.InvokeAsync<PlayResult>(method, id)
                : await _hub.InvokeAsync<PlayResult>(method);
        }
        catch (Exception ex)
        {
            return PlayResult.Fail(PlayFailure.Other, ex.Message);
        }
    }

    private async Task InvokeAsync(string method)
    {
        if (_hub is not { State: HubConnectionState.Connected })
        {
            return;
        }

        try
        {
            await _hub.InvokeAsync(method);
        }
        catch (Exception)
        {
            // Nothing to report: stopping and leaving are best-effort, and the tray already
            // shows whether the link is up.
        }
    }

    private void Set(LinkState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private async Task DisposeHubAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }

    public async ValueTask DisposeAsync() => await DisposeHubAsync();
}
