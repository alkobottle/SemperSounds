using MudBlazor.Services;
using SemperSounds.Core.Configuration;
using SemperSounds.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Configuration. In the container these come from env vars (Discord__BotToken, Soundboard__DataPath, ...);
// in development from appsettings.Development.json. ValidateOnStart turns a missing token into a
// startup failure with a clear message instead of a confusing runtime error later.
builder.Services.AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection(DiscordOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SoundboardOptions>()
    .Bind(builder.Configuration.GetSection(SoundboardOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection(AppOptions.SectionName));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
