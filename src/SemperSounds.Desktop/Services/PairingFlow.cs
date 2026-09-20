using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace SemperSounds.Desktop.Services;

public sealed record PairingResult(bool IsSuccess, string Token, string UserName, string Error)
{
    public static PairingResult Fail(string error) => new(false, string.Empty, string.Empty, error);
}

/// <summary>
/// Pairs this machine with the server, using the user's real browser.
/// </summary>
/// <remarks>
/// <para>
/// The browser is the point. Signing in is Discord's OAuth flow, which the server already
/// runs, already holds the client secret for, and already uses to reject anyone who is not in
/// the guild — and the user is probably signed in there already. A desktop app cannot do that
/// itself: Discord's token exchange demands the client secret, and Discord does not support
/// PKCE for public clients, so an app doing this directly would have to ship a secret that is
/// not a secret.
/// </para>
/// <para>
/// So the server brokers it. What comes back to the loopback listener is a one-time code, not
/// a credential, and exchanging that code requires a verifier this process never published —
/// which is what stops another program on the same machine from watching the callback port and
/// taking the pairing for itself.
/// </para>
/// </remarks>
public sealed class PairingFlow(HttpClient http)
{
    /// <summary>
    /// How long to wait for the browser. Generous because the round trip may include signing
    /// in to Discord from scratch, which is not a thing to rush somebody through.
    /// </summary>
    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromMinutes(5);

    public async Task<PairingResult> PairAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var server))
        {
            return PairingResult.Fail("That server address is not a valid URL.");
        }

        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

        using var listener = new HttpListener();
        var port = FreePort();
        var redirectUri = $"http://127.0.0.1:{port}/callback";

        // The prefix keeps the trailing slash HttpListener requires; the redirect_uri must not
        // have one, or it will not match what the server was told.
        listener.Prefixes.Add($"http://127.0.0.1:{port}/callback/");
        listener.Prefixes.Add($"http://127.0.0.1:{port}/callback");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            return PairingResult.Fail($"Could not listen for the browser's reply: {ex.Message}");
        }

        var authorize = new UriBuilder(server)
        {
            Path = "/device/authorize",
            Query = $"redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    $"&state={Uri.EscapeDataString(state)}" +
                    $"&code_challenge={Uri.EscapeDataString(challenge)}",
        }.Uri;

        Process.Start(new ProcessStartInfo(authorize.ToString()) { UseShellExecute = true });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BrowserTimeout);

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return PairingResult.Fail("Pairing timed out. Start again when you are ready.");
        }

        var query = context.Request.QueryString;
        var code = query["code"];
        var returnedState = query["state"];

        // Compared in fixed time and checked before anything is sent anywhere: the state is
        // what ties this reply to the request this process made.
        var stateMatches = returnedState is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(returnedState), Encoding.UTF8.GetBytes(state));

        if (!stateMatches || string.IsNullOrEmpty(code))
        {
            await RespondAsync(context, "Pairing failed. You can close this tab and try again.");
            return PairingResult.Fail("The browser's reply did not match this pairing attempt.");
        }

        try
        {
            var response = await http.PostAsJsonAsync(
                new Uri(server, "/api/device/token"),
                new { code, verifier, redirectUri },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var problem = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
                await RespondAsync(context, "Pairing failed. You can close this tab.");
                return PairingResult.Fail(problem?.Error ?? $"The server refused the pairing ({(int)response.StatusCode}).");
            }

            var issued = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (issued is null)
            {
                await RespondAsync(context, "Pairing failed. You can close this tab.");
                return PairingResult.Fail("The server's reply could not be read.");
            }

            await RespondAsync(context, $"Paired as {issued.UserName}. You can close this tab.");
            return new PairingResult(true, issued.Token, issued.UserName, string.Empty);
        }
        catch (HttpRequestException ex)
        {
            await RespondAsync(context, "Pairing failed. You can close this tab.");
            return PairingResult.Fail($"Could not reach the server: {ex.Message}");
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Asks the OS for a port nobody is using, rather than guessing one.</summary>
    private static int FreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    private static async Task RespondAsync(HttpListenerContext context, string message)
    {
        var body = Encoding.UTF8.GetBytes($$"""
            <!doctype html><meta charset="utf-8"><title>SemperSounds</title>
            <body style="font-family: system-ui; display: grid; place-items: center; height: 100vh; margin: 0">
            <p>{{WebUtility.HtmlEncode(message)}}</p>
            """);

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body);
        context.Response.Close();
    }

    private sealed record TokenResponse(string Token, string UserId, string UserName);

    private sealed record ErrorResponse(string Error);
}
