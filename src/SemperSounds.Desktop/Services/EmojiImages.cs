using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace SemperSounds.Desktop.Services;

/// <summary>
/// Fetches custom Discord emoji, once each.
/// </summary>
/// <remarks>
/// <para>
/// A standard emoji is a character and the font draws it. A custom one is an image on Discord's
/// CDN, and the sound carries only its canonical form — so without this the board shows
/// <c>:kappa:</c> where the server shows a picture, which is what a library full of custom
/// emoji looks like here.
/// </para>
/// <para>
/// The cache holds the <em>task</em> rather than the bitmap, so a list rendering forty rows at
/// once makes one request per distinct emoji rather than forty. A failure is cached too: a URL
/// that 404s will keep doing so, and retrying it per repaint would be a request storm for a
/// picture that is never coming.
/// </para>
/// </remarks>
public sealed class EmojiImages(HttpClient http)
{
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _cache = new(StringComparer.Ordinal);

    public Task<Bitmap?> GetAsync(string url) => _cache.GetOrAdd(url, LoadAsync);

    private async Task<Bitmap?> LoadAsync(string url)
    {
        try
        {
            // Buffered first: Bitmap wants a seekable stream, and the network one is not.
            var bytes = await http.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            // Never throws. The row falls back to the text form, which is strictly better than
            // an emoji taking the window down.
            return null;
        }
    }
}
