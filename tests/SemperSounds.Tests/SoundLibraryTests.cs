using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SemperSounds.Core.Audio;
using SemperSounds.Core.Configuration;
using SemperSounds.Core.Data;
using SemperSounds.Core.Sounds;

namespace SemperSounds.Tests;

public sealed class SoundLibraryTests : IDisposable
{
    private readonly string _dataPath =
        Path.Combine(Path.GetTempPath(), "sempersounds-tests", Guid.NewGuid().ToString("N"));

    private readonly SoundboardDbContext _db;

    // In-memory SQLite, kept alive by holding the connection open. A file-backed test
    // database would keep a pooled handle open and block the temp-directory cleanup.
    private readonly SqliteConnection _connection;

    public SoundLibraryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SoundboardDbContext>()
            .UseSqlite(_connection)
            .Options;

        Directory.CreateDirectory(_dataPath);
        _db = new SoundboardDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }

    private sealed class StubProbe(AudioProbeResult result) : IAudioProbe
    {
        public Task<AudioProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    /// <summary>Stands in for ffmpeg by writing plausible output files.</summary>
    private sealed class StubTranscoder : IAudioTranscoder
    {
        public double LastStart { get; private set; }
        public double? LastLength { get; private set; }

        public Task TranscodeAsync(string sourcePath, string pcmDestinationPath, string previewDestinationPath,
            double startSeconds = 0, double? lengthSeconds = null, CancellationToken cancellationToken = default)
        {
            LastStart = startSeconds;
            LastLength = lengthSeconds;
            File.WriteAllBytes(pcmDestinationPath, new byte[AudioFormat.BytesPerFrame]);
            File.WriteAllBytes(previewDestinationPath, [0x49, 0x44, 0x33]);
            return Task.CompletedTask;
        }
    }

    private SoundLibrary CreateLibrary(AudioProbeResult probeResult, IAudioTranscoder? transcoder = null)
    {
        var soundboardOptions = Options.Create(new SoundboardOptions
        {
            DataPath = _dataPath,
            MaxDurationSeconds = 5.0,
            DurationToleranceSeconds = 0.25,
            MaxUploadBytes = 10 * 1024 * 1024,
        });

        return new SoundLibrary(
            _db,
            new UploadValidator(new StubProbe(probeResult), soundboardOptions),
            transcoder ?? new StubTranscoder(),
            soundboardOptions,
            NullLogger<SoundLibrary>.Instance);
    }

    private static MemoryStream Upload(int bytes = 4096) => new(new byte[bytes]);

    [Fact]
    public async Task ValidUpload_StoresRowAndBothAudioFiles()
    {
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(2.5)));

        var result = await library.AddAsync(Upload(), "airhorn.mp3", "Airhorn", "meme, loud", 42, "alkobot");

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(await _db.Sounds.ToListAsync());
        Assert.Equal("Airhorn", stored.Name);
        Assert.Equal(2500, stored.DurationMs);
        Assert.Equal(42ul, stored.UploaderId);
        Assert.Equal("meme,loud", stored.Tags);
        Assert.True(File.Exists(Path.Combine(_dataPath, "sounds", stored.PcmFileName)));
        Assert.True(File.Exists(Path.Combine(_dataPath, "sounds", stored.PreviewFileName)));
    }

    [Fact]
    public async Task RejectedUpload_LeavesNoRowAndNoFilesBehind()
    {
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(45)));

        var result = await library.AddAsync(Upload(), "podcast.mp3", "Podcast", "", 42, "alkobot");

        Assert.False(result.IsSuccess);
        Assert.Empty(await _db.Sounds.ToListAsync());

        // Temp files are written before validation can probe them, so cleanup is the
        // whole point: a server that accumulates rejected uploads fills its volume.
        var soundsDir = Path.Combine(_dataPath, "sounds");
        var leftovers = Directory.Exists(soundsDir) ? Directory.GetFiles(soundsDir) : [];
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task FailedTranscode_LeavesNoRowBehind()
    {
        var library = CreateLibrary(
            new AudioProbeResult(true, TimeSpan.FromSeconds(2)),
            new ThrowingTranscoder());

        var result = await library.AddAsync(Upload(), "broken.mp3", "Broken", "", 42, "alkobot");

        Assert.False(result.IsSuccess);
        Assert.Empty(await _db.Sounds.ToListAsync());
    }

    private sealed class ThrowingTranscoder : IAudioTranscoder
    {
        public Task TranscodeAsync(string sourcePath, string pcmDestinationPath, string previewDestinationPath,
            double startSeconds = 0, double? lengthSeconds = null, CancellationToken cancellationToken = default) =>
            throw new FfmpegException("ffmpeg exploded");
    }

    [Fact]
    public async Task TrimmedUpload_PassesTheWindowToFfmpegAndStoresTheTrimmedDuration()
    {
        var transcoder = new StubTranscoder();
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(10.4)), transcoder);

        var result = await library.AddAsync(
            Upload(), "long.mp3", "Bit", "", 42, "alkobot", "🔥", new TrimRequest(2.1, 3.0));

        Assert.True(result.IsSuccess);
        Assert.Equal(2.1, transcoder.LastStart);
        Assert.Equal(3.0, transcoder.LastLength);
        // The stored duration is the kept window, not the source.
        Assert.Equal(3000, result.Sound!.DurationMs);
    }

    [Fact]
    public async Task UploadWithoutEmoji_GetsTheDefault()
    {
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(1)));

        var result = await library.AddAsync(Upload(), "beep.mp3", "Beep", "", 42, "alkobot");

        Assert.Equal(SoundEmoji.DefaultEmoji, result.Sound!.Emoji);
    }

    [Fact]
    public async Task Update_PersistsEmojiNameAndTags()
    {
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(1)));
        var added = await library.AddAsync(Upload(), "beep.mp3", "Beep", "old", 42, "alkobot");

        var updated = await library.UpdateAsync(added.Sound!.Id, "Airhorn", "meme, loud", "<:kekw:123>");

        Assert.True(updated);
        var stored = Assert.Single(await _db.Sounds.ToListAsync());
        Assert.Equal("Airhorn", stored.Name);
        Assert.Equal("meme,loud", stored.Tags);
        Assert.Equal("<:kekw:123>", stored.Emoji);
    }

    [Fact]
    public async Task Update_WithGarbageEmoji_FallsBackToDefaultRatherThanStoringIt()
    {
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(1)));
        var added = await library.AddAsync(Upload(), "beep.mp3", "Beep", "", 42, "alkobot", "🔥");

        await library.UpdateAsync(added.Sound!.Id, "Beep", "", "definitely not an emoji");

        var stored = Assert.Single(await _db.Sounds.ToListAsync());
        Assert.Equal(SoundEmoji.DefaultEmoji, stored.Emoji);
    }

    [Fact]
    public async Task TagUsage_CountsAcrossSoundsAndOrdersByPopularity()
    {
        // Ordering by usage is the anti-drift mechanism: the established tag surfaces
        // above an obscure near-duplicate while you are still typing.
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(1)));
        await library.AddAsync(Upload(), "a.mp3", "A", "meme, valorant", 42, "alkobot");
        await library.AddAsync(Upload(), "b.mp3", "B", "meme, airplane", 42, "alkobot");
        await library.AddAsync(Upload(), "c.mp3", "C", "meme", 42, "alkobot");

        var usage = await library.GetTagUsageAsync();

        Assert.Equal("meme", usage[0].Tag);
        Assert.Equal(3, usage[0].Count);
        // The two single-use tags tie on count, so they fall back to alphabetical order.
        Assert.Equal(["airplane", "valorant"], usage.Skip(1).Select(u => u.Tag));
    }

    [Fact]
    public async Task TagUsage_IsEmptyWhenNothingIsTagged()
    {
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(1)));
        await library.AddAsync(Upload(), "a.mp3", "A", "", 42, "alkobot");

        Assert.Empty(await library.GetTagUsageAsync());
    }

    [Fact]
    public async Task Delete_RemovesRowAndFiles()
    {
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(1)));
        var added = await library.AddAsync(Upload(), "beep.mp3", "Beep", "", 42, "alkobot");
        var soundId = added.Sound!.Id;

        var deleted = await library.DeleteAsync(soundId);

        Assert.True(deleted);
        Assert.Empty(await _db.Sounds.ToListAsync());
        Assert.Empty(Directory.GetFiles(Path.Combine(_dataPath, "sounds")));
    }

    [Fact]
    public async Task GetRecentPlays_OrdersNewestFirst_AgainstRealSqlite()
    {
        // Ordering by a DateTimeOffset column is not translatable by the SQLite provider,
        // and the earlier tests missed it by querying the DbSet directly instead of going
        // through this method. It only surfaced when the home page first rendered.
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(1)));
        var added = await library.AddAsync(Upload(), "beep.mp3", "Beep", "", 42, "alkobot");
        var sound = added.Sound!;

        var log = new ActivityLog(_db);
        await log.LogPlayAsync(sound, userId: 1, userName: "first", channelId: 99, channelName: "VAL");
        await Task.Delay(10);
        await log.LogPlayAsync(sound, userId: 2, userName: "second", channelId: 99, channelName: "VAL");

        var recent = await log.GetRecentAsync(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal("second", recent[0].UserName);
        Assert.Equal("first", recent[1].UserName);
    }

    [Fact]
    public async Task GetAll_OrdersByName_AgainstRealSqlite()
    {
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(1)));
        await library.AddAsync(Upload(), "z.mp3", "Zebra", "", 42, "alkobot");
        await library.AddAsync(Upload(), "a.mp3", "Airhorn", "", 42, "alkobot");

        var all = await library.GetAllAsync();

        Assert.Equal(["Airhorn", "Zebra"], all.Select(s => s.Name));
    }

    [Fact]
    public async Task PlayLog_SurvivesDeletionOfItsSound()
    {
        var library = CreateLibrary(new AudioProbeResult(true, TimeSpan.FromSeconds(1)));
        var added = await library.AddAsync(Upload(), "beep.mp3", "Beep", "", 42, "alkobot");
        var sound = added.Sound!;

        var log = new ActivityLog(_db);
        await log.LogPlayAsync(sound, userId: 7, userName: "mace", channelId: 99, channelName: "VAL");
        await library.DeleteAsync(sound.Id);

        var entry = Assert.Single(await _db.ActivityLog.ToListAsync());
        Assert.Equal("Beep", entry.SoundName);
        Assert.Equal(7ul, entry.UserId);
        Assert.Equal("mace", entry.UserName);
    }
}
