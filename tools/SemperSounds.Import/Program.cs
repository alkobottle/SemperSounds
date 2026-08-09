using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SemperSounds.Core.Audio;
using SemperSounds.Core.Configuration;
using SemperSounds.Core.Data;
using SemperSounds.Core.Sounds;

// Bulk-imports a folder of audio files described by a manifest.json, reusing the same
// pipeline as the web upload: ffprobe validation, loudness normalization, and the PCM plus
// mp3 pair. Written to migrate a Discord soundboard export, where re-uploading by hand
// would mean re-entering every name and emoji.

var folder = args.FirstOrDefault();
if (folder is null)
{
    Console.Error.WriteLine("""
        usage: SemperSounds.Import <folder> [--data <path>] [--dry-run]

          <folder>   contains manifest.json and the audio files it names
          --data     data directory; defaults to $Soundboard__DataPath or /data
          --dry-run  report what would happen, write nothing
        """);
    return 1;
}

var dryRun = args.Contains("--dry-run");
var dataPath = ArgValue("--data")
    ?? Environment.GetEnvironmentVariable("Soundboard__DataPath")
    ?? "/data";

var manifestPath = Path.Combine(folder, "manifest.json");
if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"No manifest.json in {folder}");
    return 1;
}

var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(
    await File.ReadAllTextAsync(manifestPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

Console.WriteLine($"manifest: {entries.Count} entries");
Console.WriteLine($"data:     {Path.GetFullPath(dataPath)}");
if (dryRun)
{
    Console.WriteLine("dry run: nothing will be written");
}

var options = Options.Create(new SoundboardOptions { DataPath = dataPath });

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));
services.AddSingleton(options);
services.AddDbContext<SoundboardDbContext>(o => o.UseSqlite($"Data Source={options.Value.DatabasePath}"));
services.AddScoped<IAudioProbe, FfmpegAudioProbe>();
services.AddScoped<IAudioTranscoder, FfmpegAudioTranscoder>();
services.AddScoped<UploadValidator>();
services.AddScoped<SoundLibrary>();

using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var db = scope.ServiceProvider.GetRequiredService<SoundboardDbContext>();
await db.Database.MigrateAsync();

var library = scope.ServiceProvider.GetRequiredService<SoundLibrary>();

// Existing names, so re-running the import does not duplicate everything.
var existing = (await db.Sounds.Select(s => s.Name).ToListAsync())
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

int imported = 0, skipped = 0, failed = 0;

foreach (var entry in entries)
{
    var name = string.IsNullOrWhiteSpace(entry.Name)
        ? Path.GetFileNameWithoutExtension(entry.File)
        : entry.Name.Trim();

    if (existing.Contains(name))
    {
        Console.WriteLine($"  skip    {name}  (already present)");
        skipped++;
        continue;
    }

    var path = Path.Combine(folder, entry.File);
    if (!File.Exists(path))
    {
        Console.WriteLine($"  MISSING {name}  ({entry.File})");
        failed++;
        continue;
    }

    if (dryRun)
    {
        Console.WriteLine($"  would   {name}  {entry.Emoji}");
        imported++;
        continue;
    }

    await using var stream = File.OpenRead(path);
    var result = await library.AddAsync(
        stream, entry.File, name, entry.Tags ?? string.Empty,
        entry.UploaderId, entry.UploaderName ?? "unknown", entry.Emoji ?? string.Empty);

    if (result.IsSuccess)
    {
        Console.WriteLine($"  ok      {name}  {entry.Emoji}");
        existing.Add(name);
        imported++;
    }
    else
    {
        Console.WriteLine($"  FAILED  {name}: {result.Error}");
        failed++;
    }
}

Console.WriteLine($"\nimported {imported}, skipped {skipped}, failed {failed}");
return failed > 0 ? 1 : 0;

string? ArgValue(string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

internal sealed record ManifestEntry
{
    public required string File { get; init; }
    public string? Name { get; init; }
    public string? Emoji { get; init; }
    public string? Tags { get; init; }

    /// <summary>Discord snowflake; arrives as a JSON string.</summary>
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong UploaderId { get; init; }

    public string? UploaderName { get; init; }
}

/// <summary>Discord sends IDs as strings to survive JavaScript's number precision.</summary>
internal sealed class SnowflakeConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => ulong.TryParse(reader.GetString(), out var v) ? v : 0,
            JsonTokenType.Number => reader.GetUInt64(),
            _ => 0,
        };

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
