using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using SemperSounds.Core.Audio;
using SemperSounds.Contracts;
using SemperSounds.Core.Configuration;
using SemperSounds.Core.Devices;
using SemperSounds.Core.Data;
using SemperSounds.Core.EntrySounds;
using SemperSounds.Core.Preferences;
using SemperSounds.Core.Sounds;
using SemperSounds.Core.Statistics;
using SemperSounds.Web.Components;
using SemperSounds.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration. In the container these come from env vars (Discord__BotToken, Soundboard__DataPath, ...);
// in development from appsettings.Development.json. ValidateOnStart turns a missing token into a
// startup failure with a clear message instead of a confusing runtime error later.
builder.Services.AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection(DiscordOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Catches a placeholder or mistyped token at startup with an actionable message,
// instead of an ArgumentException thrown from inside DI construction.
builder.Services.AddSingleton<IValidateOptions<DiscordOptions>, DiscordOptionsValidator>();

builder.Services.AddOptions<SoundboardOptions>()
    .Bind(builder.Configuration.GetSection(SoundboardOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection(AppOptions.SectionName));

var soundboardOptions = builder.Configuration.GetSection(SoundboardOptions.SectionName).Get<SoundboardOptions>()
    ?? new SoundboardOptions();

Directory.CreateDirectory(soundboardOptions.DataPath);
Directory.CreateDirectory(soundboardOptions.SoundsPath);

builder.Services.AddDbContext<SoundboardDbContext>(options =>
    options.UseSqlite($"Data Source={soundboardOptions.DatabasePath}"));

// Keep the Data Protection keys on the mounted volume. They default to a directory inside
// the container, which is discarded on every rebuild — that invalidates every auth cookie,
// so each deployment silently signs everyone out.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(soundboardOptions.DataPath, "keys")))
    .SetApplicationName("SemperSounds");

// Audio pipeline. The ffmpeg wrappers sit behind interfaces so upload validation
// stays testable without spawning processes.
builder.Services.AddScoped<IAudioProbe, FfmpegAudioProbe>();
builder.Services.AddScoped<IAudioTranscoder, FfmpegAudioTranscoder>();
builder.Services.AddScoped<UploadValidator>();
builder.Services.AddScoped<SoundLibrary>();
builder.Services.AddScoped<FavoriteLibrary>();
builder.Services.AddScoped<ActivityLog>();
builder.Services.AddScoped<PlayStatistics>();
builder.Services.AddScoped<EntrySoundLibrary>();
builder.Services.AddScoped<EntrySoundAdmin>();
builder.Services.AddScoped<UserPreferenceStore>();
builder.Services.AddScoped<DeviceTokenStore>();

// Discord side. All singletons: one gateway connection and one voice connection
// serve every browser session.
builder.Services.AddSingleton<SoundboardEvents>();
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());
builder.Services.AddSingleton<GuildUserDirectory>();
builder.Services.AddSingleton<VoiceStateTracker>();
builder.Services.AddSingleton<GuildEmojiProvider>();
builder.Services.AddSingleton<PlaybackService>();
builder.Services.AddSingleton<IGuildPermissions, GuildPermissionProvider>();

// Registered twice on purpose, like DiscordBotService above: this only subscribes to an
// event, and a singleton nobody resolves is never constructed — which would leave entry
// sounds silently never firing.
builder.Services.AddSingleton<EntrySoundCoordinator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EntrySoundCoordinator>());

builder.Services.AddDesktopCompanion();

builder.Services.AddSemperSoundsAuthentication(builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddSignalR();

var app = builder.Build();

// Must run before authentication: behind a TLS-terminating proxy the app would
// otherwise build an http:// OAuth redirect_uri and Discord would reject it.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
// The proxy is not known by address in a container network, so accept from any hop.
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

// When the public URL is pinned, rewrite scheme and host on the way in. Every URL the
// app generates then matches what Discord has registered — including the redirect_uri
// sent during the token exchange, which a redirect-only fix would miss.
var appOptions = app.Services.GetRequiredService<IOptions<AppOptions>>().Value;
if (appOptions.HasPublicBaseUrl && Uri.TryCreate(appOptions.PublicBaseUrl, UriKind.Absolute, out var publicUri))
{
    app.Use((context, next) =>
    {
        context.Request.Scheme = publicUri.Scheme;
        context.Request.Host = new HostString(publicUri.Authority);
        return next();
    });
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SoundboardDbContext>();
    await db.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Everything a machine talks to has to receive its own status code. The middleware above
// re-executes any 4xx/5xx that has no body into the Blazor not-found page, which would hand
// a paired desktop client an HTML document where it expects the 401 that tells it to pair
// again — and burn a render doing it. ASP.NET Core 10 has no endpoint-level opt-out, only an
// MVC filter attribute, so the feature is switched off by path right after the middleware
// that installs it.
app.Use((context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hubs") || context.Request.Path.StartsWithSegments("/api"))
    {
        var statusCodePages = context.Features.Get<IStatusCodePagesFeature>();
        if (statusCodePages is not null)
        {
            statusCodePages.Enabled = false;
        }
    }

    return next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// SkipStatusCodePages because UseStatusCodePagesWithReExecute above re-executes any bodyless
// 4xx into the Blazor not-found page — which would hand a desktop client an HTML document in
// place of the 401 that tells it to re-pair.
app.MapHub<DesktopHub>(DesktopHubMethods.Route);
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Public and unauthenticated on purpose: it doubles as the way to check which commit is
// actually live, without needing to sign in.
app.MapGet("/healthz", (IOptions<AppOptions> app) => Results.Ok(new
{
    status = "ok",
    version = app.Value.DisplayVersion,
    builtAt = app.Value.BuiltAt,
}));

// Anonymous on purpose: the caller is a desktop app that has no cookie and is not yet holding
// any credential. What authorizes it is the one-time code, which only exists because a
// signed-in guild member approved the pairing in their browser a moment ago, plus the verifier
// proving this is the same app that started the flow.
app.MapPost("/api/device/token", async (
    DeviceTokenExchangeRequest request, DeviceCodeStore codes, DeviceTokenStore tokens, CancellationToken cancellationToken) =>
{
    var pending = codes.Redeem(request.Code, request.Verifier, request.RedirectUri);
    if (pending is null)
    {
        // One answer for unknown, expired, already-used and wrong-verifier alike: telling a
        // caller which of those it hit is telling it how to search.
        return Results.Json(new { error = "That pairing code is not valid any more. Start again from the app." },
            statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        var token = await tokens.IssueAsync(pending.UserId, pending.UserName, pending.DeviceName, cancellationToken);

        // The only time the plaintext exists. Everything after this works from its hash.
        return Results.Json(new DeviceTokenExchangeResponse(token, pending.UserId.ToString(), pending.UserName));
    }
    catch (InvalidOperationException ex)
    {
        // Thrown when the user is at their device cap, which is a refusal rather than a fault.
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
    }
});

app.MapGet("/login", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = returnUrl ?? "/board" },
        [DiscordAuthenticationDefaults.AuthenticationScheme]));

app.MapPost("/logout", () =>
    Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme]));

// Serves the normalized mp3 for in-browser preview. Signed-in guild members only,
// so the library is not a public file host.
app.MapGet("/sounds/{id:guid}/preview", async (
    Guid id, SoundLibrary library, IOptions<SoundboardOptions> options, CancellationToken cancellationToken) =>
{
    var sound = await library.FindAsync(id, cancellationToken);
    if (sound is null)
    {
        return Results.NotFound();
    }

    var path = Path.Combine(options.Value.SoundsPath, sound.PreviewFileName);

    // GetFullPath is load-bearing: Results.File serves a file from disk only when the
    // path is rooted, and silently reinterprets a relative one as a virtual path under
    // wwwroot. SoundsPath is already rooted; this keeps it true no matter what.
    return File.Exists(path)
        ? Results.File(Path.GetFullPath(path), "audio/mpeg", enableRangeProcessing: true)
        : Results.NotFound();
})
// Both schemes, not just the cookie. The desktop client fetches the same mp3 to preview a
// clip locally before binding a key to it, and it holds a device token rather than a cookie.
// Membership is still required either way: a device token is only ever issued to somebody who
// completed the Discord sign-in this endpoint would otherwise demand.
.RequireAuthorization(new AuthorizeAttribute
{
    AuthenticationSchemes = $"{CookieAuthenticationDefaults.AuthenticationScheme},{DeviceTokenDefaults.Scheme}",
});

app.Run();

/// <param name="Verifier">
/// The secret half of the challenge published when the flow started. Any local process can
/// watch the loopback port and race for the code; only the app that began pairing has this.
/// </param>
internal sealed record DeviceTokenExchangeRequest(string Code, string Verifier, string RedirectUri);

/// <param name="UserId">A string: a Discord snowflake exceeds what a JSON number holds exactly.</param>
internal sealed record DeviceTokenExchangeResponse(string Token, string UserId, string UserName);
