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
| Upload limit | 5 seconds (0.25s tolerance for encoder padding), 10 MB |
| Loudness | Normalized at upload, so no clip is ten times louder than the rest |

Uploads are converted once at upload into raw 48 kHz stereo PCM plus a normalized mp3
preview. Playback then never spawns a process — it reads ~1 MB off disk and mixes it.

## Discord setup

At <https://discord.com/developers/applications>:

1. **New Application.**
2. **Bot → Reset Token** → this is `Discord__BotToken`. It is not the client secret.
3. **OAuth2** → copy Client ID and Client Secret.
4. **OAuth2 → Redirects** → add `https://your-domain/signin-discord`
   (and `http://localhost:5219/signin-discord` for local development).
5. **Invite the bot** with the `bot` scope and the **Connect** and **Speak** permissions.
6. In Discord, enable Developer Mode, right-click your server → **Copy Server ID** →
   this is `Discord__GuildId`.

No privileged intents are needed. The bot uses Guilds and Guild Voice States, both of
which are on by default.

## Running with Docker

```bash
cp .env.example .env      # fill in the four Discord values and your public URL
docker compose up -d --build
```

The container listens on `127.0.0.1:8080` and expects a reverse proxy in front of it to
terminate TLS. Point your proxy at it and make sure it sets `X-Forwarded-Proto`.

Sounds and the database live in `./data`, mounted at `/data`. **Back this up** — deleting
it deletes every uploaded sound.

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

Leave `App__PublicBaseUrl` empty in development so Kestrel's own URL is used.

```bash
dotnet test            # unit tests, no Discord or ffmpeg required
```

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
| `Soundboard__MaxDurationSeconds` | `5` | Upload length limit |
| `Soundboard__MaxUploadBytes` | `10485760` | Upload size limit |
| `Soundboard__PerUserCooldownSeconds` | `0` | Anti-spam delay between plays, per user |
| `Soundboard__IdleLeaveMinutes` | `10` | Auto-disconnect after the channel empties |

## Layout

```
src/SemperSounds.Core/    Domain: PcmMixer, upload validation, ffmpeg wrappers, EF model
src/SemperSounds.Web/     Blazor UI, Discord gateway + voice, auth
tests/SemperSounds.Tests/ Unit tests
```

The mixer and upload validator live in `Core` specifically so they can be tested without
a Discord connection or a browser.
