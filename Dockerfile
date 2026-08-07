# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on project files alone, so the layer caches across source edits.
COPY SemperSounds.slnx ./
COPY src/SemperSounds.Core/SemperSounds.Core.csproj src/SemperSounds.Core/
COPY src/SemperSounds.Web/SemperSounds.Web.csproj src/SemperSounds.Web/
COPY tests/SemperSounds.Tests/SemperSounds.Tests.csproj tests/SemperSounds.Tests/
RUN dotnet restore src/SemperSounds.Web/SemperSounds.Web.csproj

COPY . .
RUN dotnet publish src/SemperSounds.Web/SemperSounds.Web.csproj \
    -c Release -o /app --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# ffmpeg   - upload duration probing, loudness normalization, PCM conversion
# libopus  - NetCord encodes voice frames through it
# libsodium - NetCord's voice encryption
# None of these are NuGet packages; without them voice fails at runtime, not build.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ffmpeg \
        libopus0 \
        libsodium23 \
        curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Sounds and the SQLite database live here. Mount a volume or uploads die with
# the container.
RUN mkdir -p /data && chown -R app:app /data
VOLUME /data

# TLS is terminated by the reverse proxy in front of this, so plain HTTP here.
ENV ASPNETCORE_URLS=http://+:8080 \
    Soundboard__DataPath=/data
EXPOSE 8080

USER app

HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/healthz || exit 1"]

ENTRYPOINT ["dotnet", "SemperSounds.Web.dll"]
