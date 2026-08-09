# SemperSounds

A self-hosted Discord soundboard without the sound limit.

Discord caps how many sounds a server's built-in soundboard can hold. This replaces it: a
bot joins your voice channel, and a web page lets anyone in that channel fire uploaded
clips into it. Sounds overlap the way the real soundboard does, and everything is logged.

- **Backend:** .NET 10 + [NetCord](https://netcord.dev) (gateway and voice)
- **UI:** Blazor Web App (InteractiveServer) + [MudBlazor](https://mudblazor.com)
- **Storage:** SQLite + files on a mounted volume
- **Audio:** ffmpeg at upload time only

## How it works

| Thing | Behaviour |
|---|---|
| Which channel | The bot joins whatever voice channel the person pressing **Join** is in |
| Who may play | Only people currently in the bot's voice channel |
| Who may upload/delete | Anyone in the server |
| Overlapping sounds | They mix, like the real soundboard. **Stop all** silences everything |
| Upload limit | 5 seconds of kept audio (0.25s tolerance for encoder padding), 10 MB |
| Longer files | Fine — trim them in the browser on a waveform before uploading. Sources up to 5 minutes |
| Loudness | Normalized at upload, so no clip is ten times louder than the rest |
| Finding things | Live search over names, tags and emoji, plus tag filters. Tags autocomplete against those already in use, so near-duplicates do not pile up |
| Emoji | Every sound carries one — your server's custom emoji or any standard one — and search matches it, including a custom emoji's name |
| Favourites | Each person stars up to 9 sounds and plays them with keys 1–9 |
| Leaving | The bot drops out a few seconds after the last human leaves the channel |
| Log | Sounds played, plus who summoned the bot and when it left |

Uploads are converted once at upload into raw 48 kHz stereo PCM plus a normalized mp3
preview. Playback then never spawns a process — it reads ~1 MB off disk and mixes it.

## Discord setup

At <https://discord.com/developers/applications>:

1. **New Application.**
2. **Bot → Reset Token** → this is `Discord__BotToken`. It is not the client secret.
3. **OAuth2** → copy Client ID and Client Secret.
4. **OAuth2 → Redirects** → add `https://your-domain/signin-discord`
   (and `http://localhost:5219/signin-discord` for local development).
5. **Bot → Privileged Gateway Intents → enable Server Members Intent.** Self-enablable
   below 100 servers, no review needed. **Do not skip this**: without it Discord sends an
   effectively empty member list, so avatars and nicknames never resolve and the bot
   counts itself as a listener, meaning it never auto-disconnects from an empty channel.
   Presence and Message Content stay off.
6. **Invite the bot** with the `bot` scope and the **Connect** and **Speak** permissions.
7. In Discord, enable Developer Mode, right-click your server → **Copy Server ID** →
   this is `Discord__GuildId`.

## Running with Docker

```bash
cp .env.example .env      # fill in the four Discord values and your public URL
docker compose up -d --build
```

The container listens on `127.0.0.1:8080` and expects a reverse proxy in front of it to
terminate TLS. Point your proxy at it and make sure it sets `X-Forwarded-Proto`.

Sounds, the database and the Data Protection keys live in `./data`, mounted at `/data`.
**Back this up** — deleting it deletes every uploaded sound, and discarding the keys signs
everyone out.

### Behind the proxy

Set `App__PublicBaseUrl` to the https URL users actually visit. Without it the app builds
an `http://` OAuth redirect URI and Discord refuses the login — the most common way this
deployment goes wrong.

## Local development

```bash
cp src/SemperSounds.Web/appsettings.Development.example.json \
   src/SemperSounds.Web/appsettings.Development.json
# fill in the Discord values, then:
dotnet run --project src/SemperSounds.Web
```

**ffmpeg and ffprobe must be on your PATH** for uploads to work locally. The container
already has them. On Windows: `winget install Gyan.FFmpeg`.

### Native voice libraries

NetCord calls into three native libraries, and voice fails at runtime without them:

| Library | Where it comes from | Why |
|---|---|---|
| libdave | `libdave` NuGet package | Discord's E2EE voice protocol. NetCord loads it unconditionally — there is no way to turn it off, and it is not in any Linux distro repo. |
| libsodium | `libsodium` NuGet package | Voice encryption. Official package by libsodium's author. |
| opus | `OpusDotNet.opus.win-x64` on Windows, `libopus-dev` in the container | Opus encoding of voice frames. |

All three ship as `runtimes/<rid>/native/` assets, so a plain `dotnet run` picks them up.
In the container, opus comes from apt: use `libopus-dev` and not `libopus0`, since the
latter installs only `libopus.so.0` while .NET probes for the unversioned `libopus.so`.

Leave `App__PublicBaseUrl` empty in development so Kestrel's own URL is used.

```bash
dotnet test            # unit tests, no Discord or ffmpeg required
```

## Bulk import

Useful for migrating an existing Discord soundboard, where uploading by hand would mean
re-entering every name and emoji.

Put the audio files and a `manifest.json` in a folder, drop it under the data volume
(`./data/import`), and run the importer from the running container — it needs the ffmpeg
the image already carries:

```bash
docker compose exec sempersounds dotnet SemperSounds.Import.dll /data/import --dry-run
docker compose exec sempersounds dotnet SemperSounds.Import.dll /data/import
```

`manifest.json` is an array of:

```json
[{ "file": "01_airhorn.mp3", "name": "Airhorn", "emoji": "📣",
   "tags": "meme, loud", "uploaderId": "277501102943895552", "uploaderName": "alkobottle" }]
```

`emoji` takes either a standard emoji or Discord's `<:name:id>` form. Imports run through
the same validation and loudness normalization as the web upload, and skip any name that
already exists, so re-running is safe.

### Exporting a Discord soundboard

The bot can list the guild's sounds itself; the audio downloads from the CDN even for
sounds the server can no longer play because its boosts lapsed:

```bash
curl -H "Authorization: Bot $TOKEN" \
  https://discord.com/api/v10/guilds/$GUILD_ID/soundboard-sounds
curl -o sound.mp3 https://cdn.discordapp.com/soundboard-sounds/$SOUND_ID
```

Custom emoji come back with `emoji_id` and a null `emoji_name`, so the names have to be
resolved separately from `/guilds/{id}/emojis`.

## Configuration

Every setting binds from the config file in development and from environment variables in
the container — `Soundboard__MaxDurationSeconds` is the same knob as the nested JSON key.

| Variable | Default | Meaning |
|---|---|---|
| `Discord__BotToken` | — | Bot token (required) |
| `Discord__ClientId` / `Discord__ClientSecret` | — | OAuth2 credentials (required) |
| `Discord__GuildId` | — | The one server this instance serves (required) |
| `App__PublicBaseUrl` | empty | Public https URL; required behind a proxy |
| `Soundboard__DataPath` | `/data` | Where sounds and the database live |
| `Soundboard__MaxDurationSeconds` | `5` | Length limit on the audio that is *kept* |
| `Soundboard__MaxSourceDurationSeconds` | `300` | Longest file accepted for trimming |
| `Soundboard__MaxUploadBytes` | `10485760` | Upload size limit |
| `Soundboard__PerUserCooldownSeconds` | `0` | Anti-spam delay between plays, per user |
| `Soundboard__IdleLeaveSeconds` | `5` | Auto-disconnect this long after the last human leaves. `0` disables |
| `Soundboard__DurationToleranceSeconds` | `0.25` | Slack on the length limit; encoders pad, so a clip authored at 5.00s often probes at 5.02s |
| `Soundboard__FfmpegPath` / `Soundboard__FfprobePath` | `ffmpeg` / `ffprobe` | Override if they are not on `PATH` |
| `Soundboard__TranscodeTimeoutSeconds` | `60` | Kills a stuck ffmpeg run |

## Layout

```
src/SemperSounds.Core/     Domain: PcmMixer, upload validation, ffmpeg wrappers, EF model
src/SemperSounds.Web/      Blazor UI, Discord gateway + voice, auth
tools/SemperSounds.Import/ Bulk importer, published into the same image
tests/SemperSounds.Tests/  Unit tests
```

`/` is a public landing page carrying the link-preview card; the board itself is `/board`
and requires sign-in.

The mixer and upload validator live in `Core` specifically so they can be tested without
a Discord connection or a browser.
