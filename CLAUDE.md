# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The solution file is `SemperSounds.slnx` (.NET 10's XML format) — `dotnet build` with no
argument works, but scripts must not assume a `.sln` exists.

```bash
dotnet build SemperSounds.slnx          # 0 warnings is the expected baseline
dotnet test                             # all tests; needs neither Discord nor ffmpeg
dotnet test --filter 'FullyQualifiedName~LoudClips_ClampInsteadOfWrapping'   # one test
dotnet test --filter 'FullyQualifiedName~PcmMixerTests'                      # one class
dotnet run --project src/SemperSounds.Web
```

EF migrations live in `Core` but are generated against the `Web` startup project, so both
flags are mandatory:

```bash
dotnet ef migrations add <Name> \
  --project src/SemperSounds.Core --startup-project src/SemperSounds.Web \
  --output-dir Data/Migrations
```

Migrations apply automatically at startup (`db.Database.MigrateAsync()` in `Program.cs`).

**The app will not start without real Discord credentials.** `ValidateOnStart` plus
`DiscordOptionsValidator` reject a placeholder bot token by design. Copy
`src/SemperSounds.Web/appsettings.Development.example.json` to
`appsettings.Development.json` (gitignored) and fill it in.

**ffmpeg and ffprobe must be on PATH** for uploads to work locally; the container installs
them. Unit tests stub the `IAudioProbe`/`IAudioTranscoder` boundary, so they pass without it.

NetCord is prerelease — adding or updating it needs `dotnet add package NetCord --prerelease`.

### Native voice libraries

Voice needs libdave, libsodium and opus, and every one of them fails at *runtime* rather
than at build. libdave is Discord's E2EE voice protocol; it is internal to NetCord's
`VoiceClient` with no way to disable it, and it exists in no Linux distro repo. libdave and
libsodium therefore arrive as NuGet native assets (`runtimes/<rid>/native/`), opus from
`OpusDotNet.opus.win-x64` on Windows and `libopus-dev` in the container.

Use `libopus-dev`, never `libopus0`: the latter installs only `libopus.so.0`, while .NET
probes for the unversioned `libopus.so`. To check an image, `ldd` each native and confirm
nothing reports "not found".

### Never add --no-restore to the Dockerfile's publish

The Dockerfile restores on `.csproj` files alone to keep that layer cacheable. At that
point the SDK cannot see any Razor component or `wwwroot`, so it does **not** add the
implicit `Microsoft.AspNetCore.App.Internal.Assets` package — the one carrying
`blazor.web.js`. Publishing with `--no-restore` locks in that incomplete graph, and the
result is an app that looks fine but serves a 404 for `/_framework/blazor.web.js`, leaving
every page rendered but completely non-interactive.

This reproduces on a stock Blazor template, so it is not specific to this project. Verify
any Dockerfile change with:

```bash
docker run --rm --entrypoint /bin/sh sempersounds:latest -c "ls wwwroot/_framework/"
```

### Verifying third-party API shape

NetCord and MudBlazor docs lag their packages, and both have already been wrong here
(MudBlazor 9 renamed `ShowMessageBox` → `ShowMessageBoxAsync`). Reflect over the shipped
assembly instead of guessing — .NET 10 file-based apps make this a one-liner:

```csharp
// probe.cs, then: dotnet run probe.cs
#:package NetCord@1.0.0-beta.12
```

## Architecture

One process serves both the Blazor UI and the Discord bot, so pages reach the bot through
DI singletons — there is no IPC, message broker, or second service.

```
Blazor page → SoundLibrary (scoped)   → EF Core → SQLite      /data/sempersounds.db
            → PlaybackService (single) → PcmMixer → OpusEncodeStream → NetCord VoiceClient
            ← SoundboardEvents (in-proc pub/sub) ← VoiceStateTracker ← GatewayClient
```

### Lifetime boundary (most common source of mistakes)

`DiscordBotService`, `VoiceStateTracker`, `PlaybackService` and `SoundboardEvents` are
**singletons**. `SoundboardDbContext`, `SoundLibrary` and the ffmpeg wrappers are
**scoped**. A singleton must therefore reach the library through `IServiceScopeFactory` —
see `PlaybackService.PlayAsync`. Injecting `SoundLibrary` into a singleton directly will
capture a disposed context.

### The audio contract

`AudioFormat` (in `Core/Audio`) is the single canonical format: 48 kHz, stereo, s16le.
`BytesPerFrame` (3840 = one 20 ms frame) is the unit of exchange between `PcmMixer` and
NetCord's `OpusEncodeStream`, which is constructed with `PcmFormat.Short` to match. Changing
either side without the other produces silence or noise, not an error.

**ffmpeg runs only at upload.** Each sound is stored twice — `{id}.pcm` for the mixer and
`{id}.mp3` for browser preview — so playback is a ~1 MB disk read and never spawns a
process. Do not add transcoding to the playback path.

`PcmMixer.MixNextFrame` accumulates in `int` and clamps. Summing into `short` wraps 60000
to −5536, which turns loud overlaps into inverted noise; there is a test pinning this.
Voices are evicted when *fewer than a whole sample* remains, not when position reaches the
end — a truncated file leaves a trailing byte that would otherwise stick forever.

### Voice state is not duplicated

`VoiceStateTracker` reads NetCord's `Guild.VoiceStates` cache, which NetCord already keeps
current from every `VOICE_STATE_UPDATE`. Do not introduce a parallel dictionary; a second
copy only creates a way for the two to disagree.

### Authorization

Who may play is enforced in `PlaybackService`, not the UI. Disabled buttons are a hint; the
service is the rule. Any new play path must go through it. Rules: sign-in requires
membership of `Discord__GuildId` (checked in `OnCreatingTicket`, which calls `context.Fail`
so a non-member never receives a cookie); playing requires being in the bot's *current*
channel; uploading and deleting are open to any signed-in member, deliberately.

`PlayLogEntry` denormalizes `SoundName` and holds **no foreign key** to `Sound`, because
anyone can delete sounds and the history must survive it.

### Blazor and MudBlazor specifics

`App.razor` sets `@rendermode="InteractiveServer"` globally on `Routes` and `HeadOutlet`.
MudBlazor's `MudDialogProvider`/`MudSnackbarProvider`/`MudPopoverProvider` live in
`MainLayout` and **fail silently** — no error, dialogs simply never appear — if the layout
renders statically. Do not move to per-page render modes.

Components subscribing to `SoundboardEvents` must unsubscribe in `Dispose`; a leaked
handler pins the whole circuit. Handlers marshal via `InvokeAsync(StateHasChanged)` because
they are raised from Discord gateway threads.

### Reverse proxy

`Program.cs` ordering is load-bearing: `UseForwardedHeaders(forwardedHeaders)` — passing the
options object explicitly, since the parameterless overload reads from DI where nothing is
registered — then the `App__PublicBaseUrl` scheme/host rewrite, then `UseAuthentication`.
The rewrite must happen on the request (not just on redirects) so the `redirect_uri` sent
during the OAuth *token exchange* matches too. Getting this wrong makes Discord reject
logins in production while development works fine.

## Configuration

Config binds identically from `appsettings.Development.json` and container env vars —
`Soundboard__MaxDurationSeconds` is the same knob as the nested JSON key. Options types are
in `Core/Configuration`. The 5-second upload limit carries a 0.25 s tolerance because
encoders pad, and a clip authored as exactly 5.00 s routinely probes at 5.02 s.
