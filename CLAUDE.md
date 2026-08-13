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

**The Server Members privileged intent must be enabled in the developer portal**, or
avatars, nicknames and idle auto-leave all break silently — see the guild member cache
section below.

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

### The guild member cache needs an intent *and* an explicit request

`Guild.Users` is filled only from the `members` array of `GUILD_CREATE`, and two separate
things keep it empty:

1. Without the privileged `GuildUsers` intent — which must be enabled both in
   `DiscordBotService` and in the portal — Discord sends only the bot's own member.
2. Above Discord's `large_threshold` (default **50**) it omits members regardless. This
   guild has 51, so `GUILD_CREATE` arrived with 2 users.

`DiscordBotService` therefore calls `RequestGuildUsersAsync` on `GuildCreate` and lets the
`GuildUserChunk` handler fill the cache. Do not "fix" a future recurrence by raising
`LargeThreshold`; that only postpones the same silent failure to 250 members.

This failed *silently* in two places at once: avatars fell back to initials, and
`CountHumansIn` — which excluded bots via `Users.TryGetValue(...) && IsBot` — counted the
bot as a listener, so idle auto-leave never fired. Bot exclusion is now by
`bot.Client.Cache.User.Id`, which needs no intent, so that path survives the intent being
revoked.

### Readiness must be restored by Resume, not only Ready

`Ready` fires on a fresh IDENTIFY. A dropped socket is normally repaired by *resuming* the
session, which Discord answers with RESUMED and NetCord surfaces as a **separate `Resume`
event**. Marking the bot unusable on `Disconnect` and usable only on `Ready` therefore
latches it off after the first transient drop: the gateway reconnects and heartbeats
happily while every join and play refuses with "The bot is not connected to Discord yet",
until the process is restarted.

`GatewayReadiness` holds the state machine so the transitions are testable without a
connection, and `DiscordBotService` subscribes to `Ready`, `Resume` and `Disconnect`. This
was invisible for hours because `OnDisconnectAsync` logged nothing — confirming it meant
reading TCP counters inside the container to spot that the live socket had received only
heartbeat ACKs and never a READY payload. Both transitions now log.

### ffmpeg trim options must precede -i

`FfmpegAudioTranscoder` writes **two** outputs (PCM and mp3 preview). Options placed after
`-i` are *output* options binding only to the **next** output, so `-t` there trimmed the
PCM and left the preview running to the end of the source — a 10s source seeking 2s and
keeping 3s produced a 3.00s PCM and an 8.04s mp3. Both `-ss` and `-t` belong before `-i`,
where they are input options. `FfmpegArgumentTests` pins the ordering.

### Discord's speaking ring follows packet flow, not the flag

Lowering the speaking flag does not clear the green ring if frames keep being sent. The
pump therefore writes **nothing at all** while idle, and opens a fresh stream per speaking
burst — NetCord's voice stream normalizes speed against a clock, so resuming a long-idle
stream risks it rushing frames to catch up. `EnterSpeakingStateAsync` must still be called
on join: it readies the connection, and without it the first `SendVoiceAsync` throws
"Connection not started".

### Entry sounds are passive, and VOICE_STATE_UPDATE is not a join

The bot **never** auto-joins to play someone's entry sound. If it is not already connected
to the channel, nothing happens. That single rule is what keeps the feature from stealing
the bot away from a channel it is being used in, and from adding a ~1s connect handshake
before every clip.

Discord sends `VOICE_STATE_UPDATE` for muting, deafening and enabling video as well as for
moving, and every one of them carries the member's *current* channel. `VoiceTransitionJournal`
remembers the *previous* channel per user so an arrival can be told apart from a microphone
toggle. This does not contradict `VoiceStateTracker`'s rule against duplicating voice state:
that rule is about *current* state, which NetCord owns; the previous channel is something the
cache structurally cannot answer, because NetCord overwrites each entry in place. The journal
is seeded wholesale from `GUILD_CREATE` — without that, the first update after every reconnect
reads as everyone arriving at once.

Two more things that were easy to get wrong here:

- The destination comes from the **payload**, never the cache, so nothing depends on whether
  NetCord applies the update before or after calling the handler. For the same reason
  `CountHumansIn` takes an `exceptUserId`: excluding the arriving member makes "is anybody
  else here" the same answer either way.
- `OnVoiceStateUpdateAsync` had never filtered out the **bot's own** voice state, which cost
  nothing while the payload was discarded. Every `JoinAsync` now reads as an arrival without
  that filter.

`EntrySoundPolicy` holds every rule (enabled, snoozed, occupancy, assignment, self-mute,
block, cooldown) as pure state, so all of it is testable without a gateway. Note it treats
unknown occupancy as "do not play", the deliberate **opposite** of `IdleTimer`'s "unknown
means someone is listening" — both refuse to act on a guess, and the action differs.

The entry cooldown has its own dictionary in `EntrySoundCoordinator`, deliberately not
`PlaybackService._lastPlayed`: sharing it would mean pressing a board button silences your
own entry sound, and the admin's live setting would fight `Soundboard__PerUserCooldownSeconds`.

### A singleton that only subscribes to events is never constructed

`EntrySoundCoordinator` is registered **twice** — `AddSingleton` plus
`AddHostedService(sp => sp.GetRequiredService<...>())`, the same shape `DiscordBotService`
uses. Nothing resolves it otherwise, so the container never builds it, and the failure is
silent: entry sounds simply never fire. Any future event-only singleton needs the same.

Both `EntrySoundAdmin` and `EntrySoundCoordinator` take an optional `TimeProvider` so tests
can drive the clock. `TimeProvider` is not registered in DI; the container honours the
default value instead, which `EntrySoundRegistrationTests` pins.

### ActivityLine's switches all end in an arm meaning "Left"

All three `switch` expressions in `ActivityLine.razor` finish with `_ =>` arms that render a
departure. A new `SoundboardActivity` member therefore shows up with the logout icon reading
"sent the bot away from voice" — no error, just a wrong sentence in the log. Give every new
kind an explicit arm **before** the default. The enum values are explicit, so adding one
renumbers nothing and needs no migration.

### Failing a policy must not redirect to login

`Routes.razor`'s `NotAuthorized` used to render `RedirectToLogin` unconditionally, which is
right only for anonymous users. An authenticated user who fails a policy — the admin page —
would be sent to `/login`, handed a cookie they already hold, bounced back, and fail again:
an infinite redirect rather than an explanation. It now branches on
`context.User.Identity?.IsAuthenticated` and renders `AdminRequired` instead.

`IGuildPermissions` returns `bool?`, and **null is not "no"**. `Guild.Users` fills
asynchronously from `GuildUserChunk` seconds after every restart, so a null answer means "not
known yet". The policy fails closed on it; `AdminRequired` tells the two apart for the reader
and reloads once the member list lands.

### Play counts are derived, never stored

`PlayStatistics` (`Core/Statistics/`) aggregates the activity log; there is deliberately no
`PlayCount` column on `Sound`. A stored counter could not be retroactive without a backfill,
would drift every time a log write is swallowed — which `PlaybackService.LogActivityAsync`
does on purpose — and could answer no question about a time window. Deriving it also means
the numbers were meaningful the day the feature shipped, because
`RenamePlayLogToActivityLog` carried the old rows across.

Counts are **presses only** (`SoundboardActivity.Played`), never `EntryPlayed`: one person
rejoining voice forty times must not make their clip look popular. A consequence worth
knowing is that a sound whose only history is entry sounds shows as *never played*.

Two things this rests on, both pinned by tests because neither is obvious:

- `OccurredAt` goes through `UtcTicksConverter`, so `MAX(OccurredAt)` needs that converter
  applied **in reverse** on materialisation. When that half-works the result is not an
  exception, it is a year-0001 timestamp — a green build and a rendered page that quietly
  lies. `PerSoundStats_AggregateInSql_AgainstRealSqlite` pins it.
- `PerSoundStats_RunsInSqlRatherThanInMemory` asserts on `ToQueryString()`. A correct result
  says nothing about *where* it was computed: adding `.AsEnumerable()` makes any translation
  error disappear while dragging the whole log into memory on every board load.

Aggregates group by `SoundId`, **never by the denormalized `SoundName`** — anyone can rename
a sound, and grouping by name splits one clip into two rows the moment they do. Deleted
sounds stay in the rankings, flagged: the log holds no foreign key precisely so history
outlives a deletion, and dropping them would make the totals stop adding up.

`GetPlaysPerDayAsync` is the one aggregate that reads rows into memory, because SQLite cannot
bucket UTC ticks into days. It is bounded by a `>= since` clause, so it is O(plays in the
window) rather than O(table) — which is what makes it acceptable there and nowhere else.

`SoundPlayedNotification` carries a `Kind` so a subscriber can keep a count current without
re-running the aggregate: plays are bursty by design and every open circuit subscribes, so
re-querying per press would be a query storm. It has **no default value** — there are exactly
two places that raise it, and a default would let a future third play path count silently as
a press.

### Board sorting and filtering lives outside the page

`BoardView.Apply` (`Web/BoardPreferences.cs`) is a pure static function, not logic inside
`Home.razor`. There is no bUnit in this solution, so anything left in the page is untestable —
and the rules here are exactly the sort that break quietly.

**Every sort arm needs its name tiebreak.** That is not defensive padding: the bulk importer
stamps every clip it creates with the same `UploadedAt`, so "recently uploaded" is mostly
ties, and most of the library has never been played, so "most played" is one large tie along
the bottom. Without it the order there is arbitrary and shifts between renders.

Sorting in memory with `OrdinalIgnoreCase` deliberately differs from the old
`GetAllAsync` ordering: SQLite's default BINARY collation put every capital before every
lowercase letter, so "Zebra" sorted above "apple".

The favourites strip is **not** sorted or filtered. It renders from `Slot`, and `OnHotkey`
looks up by `Slot`, so a key keeps meaning one sound — the same reason search never touched it.

Preferences persist in `localStorage` under one versioned key. Snowflakes are stored **as
strings**: a Discord id exceeds `Number.MAX_SAFE_INTEGER`, and as a JSON number any
`JSON.parse` rounds it silently so the uploader filter matches nobody. Enums are stored by
name so renumbering cannot repoint a saved choice. `BoardPreferencesJson.Deserialize` never
throws — it runs in `OnAfterRenderAsync`, where an escaping exception kills the circuit.
Reading it there rather than in `OnInitializedAsync` is forced by prerendering, which has no
JS runtime; the cost is a brief flash of the default board.

### MudBlazor 9 charts are generic, and Data is not an array

Both changed from v8, so every sample online fails to compile — the same trap as
`ShowMessageBox` → `ShowMessageBoxAsync`. Verified by reflection over the shipped 9.8.0
assembly:

- `MudChart<T>` and `ChartSeries<T>` are **generic** (`<MudChart T="double" …>`).
- `ChartSeries<T>.Data` is a `ChartData<T>`, not `T[]`. A `double[]` works only through an
  implicit conversion.
- `ChartSeries` on the chart is `List<ChartSeries<double>>`; `ChartLabels` is `string[]`.

### There is no Bootstrap

It was removed during scaffolding, so `text-truncate`, `text-center` and friends are
**no-ops** — they silently do nothing rather than erroring, which left titles wrapping and
tiles at mismatched heights. `wwwroot/app.css` defines `ss-truncate` and `ss-clamp-2`;
check a class exists in either MudBlazor's CSS or `app.css` before using it.

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

GatewayClient → DiscordBotService → VoiceTransitionJournal → SoundboardEvents.VoiceMemberArrived
              → EntrySoundCoordinator (single) → EntrySoundPolicy → PlaybackService → PcmMixer
```

### Pages

| Route | File | Notes |
|---|---|---|
| `/` | `Landing.razor` | Public; carries the link-preview card and redirects members to `/board` |
| `/board` | `Home.razor` | The soundboard. Favourites strip, search, sort, filters, tiles, recent activity |
| `/upload` | `Upload.razor` | Waveform trimming, emoji, tags |
| `/entry-sound` | `MyEntrySound.razor` | Pick your own entry sound, self-mute, see everyone else's |
| `/admin/entry-sounds` | `AdminEntrySounds.razor` | `[Authorize(Policy = EntrySoundPolicies.Administrator)]` |
| `/log` | `Log.razor` | Raw activity, newest first |
| `/stats` | `Stats.razor` | Top sounds and users, plays per day. Loads once, no subscriptions |

Every page except `/` requires sign-in, which already requires guild membership. The file
names deliberately avoid the entity names they would otherwise shadow inside their own
`@code` block — `MyEntrySound` rather than `EntrySound`, `AdminEntrySounds` rather than
`EntrySoundSettings`. For the same reason `EntrySoundLibrary` is injected as `Entries`: a
property named `EntrySounds` collides with the `SemperSounds.Core.EntrySounds` namespace that
`_Imports.razor` pulls into scope.

Nav lives in `MainLayout.razor` **twice** — a desktop `MudHidden` button row and a mobile
`MudMenu` — so every entry has to be added in both. The bar needs roughly 690px of fixed
content on desktop, which is why Log and Stats share a "History" menu rather than taking a
slot each.

### Lifetime boundary (most common source of mistakes)

`DiscordBotService`, `VoiceStateTracker`, `PlaybackService`, `SoundboardEvents`,
`EntrySoundCoordinator` and `GuildPermissionProvider` are
**singletons**. `SoundboardDbContext`, `SoundLibrary`, `EntrySoundLibrary`, `EntrySoundAdmin`,
`PlayStatistics` and the ffmpeg wrappers are
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

The same rule extends to entry sounds: `EntrySoundAdmin` takes the **acting** user's id and
checks `IGuildPermissions` before every write, so hiding the page is not what protects it.
`PlaybackService.PlayEntrySoundAsync` skips the "you must be in the bot's channel" check —
the arriving member is in it by construction — but keeps the invariants that matter: the
target must be the bot's *current* channel, re-checked after the disk read, and the sound
must equal that user's own stored assignment. The extra authority it grants is therefore
only "an existing entry sound can be re-fired in a channel the bot already sits in".

`ActivityLogEntry` denormalizes `SoundName` and holds **no foreign key** to `Sound`,
because anyone can delete sounds and the history must survive it. `Favorite` and `EntrySound`
deliberately do the opposite — a real foreign key with cascade delete — since either pointing
at a deleted sound is only a dangling shortcut. `EntrySoundBlock` is its own table rather
than a flag on `EntrySound` for a third reason: a block must outlive the assignment, or
clearing and re-picking a sound would launder it away, and nobody without an assignment
could be blocked at all.

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
