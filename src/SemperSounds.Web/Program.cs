using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using SemperSounds.Core.Audio;
using SemperSounds.Core.Configuration;
using SemperSounds.Core.Data;
using SemperSounds.Core.Sounds;
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

// Audio pipeline. The ffmpeg wrappers sit behind interfaces so upload validation
// stays testable without spawning processes.
builder.Services.AddScoped<IAudioProbe, FfmpegAudioProbe>();
builder.Services.AddScoped<IAudioTranscoder, FfmpegAudioTranscoder>();
builder.Services.AddScoped<UploadValidator>();
builder.Services.AddScoped<SoundLibrary>();

// Discord side. All singletons: one gateway connection and one voice connection
// serve every browser session.
builder.Services.AddSingleton<SoundboardEvents>();
builder.Services.AddSingleton<DiscordBotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());
builder.Services.AddSingleton<VoiceStateTracker>();
builder.Services.AddSingleton<PlaybackService>();

builder.Services.AddSemperSoundsAuthentication(builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

var app = builder.Build();

// Must run before authentication: behind a TLS-terminating proxy the app would
// otherwise build an http:// OAuth redirect_uri and Discord would reject it.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
// The proxy is not known by address in a container network, so accept from any hop.
forwardedHeaders.KnownNetworks.Clear();
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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapGet("/login", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
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
    return File.Exists(path)
        ? Results.File(path, "audio/mpeg", enableRangeProcessing: true)
        : Results.NotFound();
}).RequireAuthorization();

app.Run();
