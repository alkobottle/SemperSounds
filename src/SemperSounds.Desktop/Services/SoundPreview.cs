using System.Net.Http.Headers;
using NAudio.Wave;

namespace SemperSounds.Desktop.Services;

/// <summary>
/// Plays a clip through this machine's own speakers, so you can hear what you are binding.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately local and deliberately not the bot. Auditioning two hundred clips to find the
/// one you want should not fire two hundred of them into a voice channel full of people, which
/// is the same reason the web board previews in the browser rather than through Discord.
/// </para>
/// <para>
/// It fetches the same <c>/sounds/{id}/preview</c> mp3 the board uses, with the device token
/// in place of a cookie. NAudio does the playing: Avalonia draws and has no audio of its own.
/// </para>
/// <para>
/// One clip at a time. A preview is a question — "is this the one?" — and overlapping answers
/// are no use, so starting a new one stops whatever was running.
/// </para>
/// </remarks>
public sealed class SoundPreview(HttpClient http, Func<string?> tokenProvider) : IDisposable
{
    private readonly Lock _gate = new();

    private WaveOut? _output;
    private Mp3FileReaderBase? _reader;
    private MemoryStream? _buffer;

    /// <summary>The clip currently being previewed, if any.</summary>
    public Guid Playing { get; private set; }

    public event Action? Changed;

    public async Task<string?> PlayAsync(Guid soundId, string serverUrl)
    {
        // A second press on the clip already previewing means "stop", which is what the same
        // button showing a stop icon has just promised.
        if (Playing == soundId)
        {
            Stop();
            return null;
        }

        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var server))
        {
            return "No server is configured.";
        }

        byte[] mp3;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, $"/sounds/{soundId}/preview"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider());

            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return $"Could not fetch that clip ({(int)response.StatusCode}).";
            }

            mp3 = await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            // Never thrown onward: the caller is an event handler, where an escape is not an
            // error message but the whole app closing.
            return $"Could not fetch that clip: {ex.Message}";
        }

        try
        {
            lock (_gate)
            {
                StopCore();

                _buffer = new MemoryStream(mp3);
                // Mp3FileReaderBase plus the ACM decompressor explicitly, which is what the
                // convenience Mp3FileReader does internally. Naming them keeps the dependency
                // at NAudio.Core plus NAudio.WinMM instead of the metapackage, which would
                // bring the whole Windows Forms runtime along for a tray app that draws none.
                _reader = new Mp3FileReaderBase(_buffer, format => new AcmMp3FrameDecompressor(format));
                _output = new WaveOut();
                _output.Init(_reader);
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Play();

                Playing = soundId;
            }

            Changed?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            Stop();
            return $"Could not play that clip: {ex.Message}";
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }

        Changed?.Invoke();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Raised when the clip runs out as well as when it is stopped, so the button has to
        // fall back to "preview" here or it would stay showing stop for ever.
        lock (_gate)
        {
            if (_output != sender)
            {
                return;
            }

            Playing = Guid.Empty;
        }

        Changed?.Invoke();
    }

    /// <summary>Tears down the current playback. Callers hold <see cref="_gate"/>.</summary>
    private void StopCore()
    {
        if (_output is not null)
        {
            // Detached first: Stop raises PlaybackStopped, and letting that run while this
            // method is mid-teardown would have it observe a half-disposed player.
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        _reader?.Dispose();
        _reader = null;

        _buffer?.Dispose();
        _buffer = null;

        Playing = Guid.Empty;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopCore();
        }
    }
}
