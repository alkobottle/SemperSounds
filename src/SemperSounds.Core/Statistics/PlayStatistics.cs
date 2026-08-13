using Microsoft.EntityFrameworkCore;
using SemperSounds.Core.Data;

namespace SemperSounds.Core.Statistics;

/// <param name="Plays">Button presses only — <see cref="SoundboardActivity.Played"/>, never
/// <see cref="SoundboardActivity.EntryPlayed"/>.</param>
/// <param name="LastPlayedAt">Null only when nobody has ever pressed it.</param>
public readonly record struct SoundPlayStats(
    Guid SoundId,
    int Plays,
    int PlaysThisWeek,
    int PlaysPreviousWeek,
    DateTimeOffset? LastPlayedAt)
{
    /// <summary>
    /// Played more this week than last, and often enough this week to be worth saying so.
    /// </summary>
    /// <remarks>
    /// Measures a rise rather than a rank. A sound that gets forty plays every week forever
    /// is popular, not trending — the most-played sort already says that — and a badge tied
    /// to a fixed top-N would always appear exactly N times and mean nothing.
    /// </remarks>
    public bool IsTrending =>
        PlaysThisWeek >= PlayStatistics.TrendingMinimumPlays && PlaysThisWeek > PlaysPreviousWeek;
}

/// <summary>
/// Aggregates over the activity log: how often each sound is actually pressed.
/// </summary>
/// <remarks>
/// Separate from <see cref="Sounds.ActivityLog"/>, which owns the write path and reads the
/// log back newest-first. That class's remark is the stated reason the table carried a single
/// index; grouped reads are a different access pattern with different indexing needs, and
/// hiding them behind an "appends only" comment would make that comment a lie.
/// <para>
/// Counts are derived rather than denormalized onto <c>Sound</c>. A stored counter could not
/// be retroactive without a backfill, would drift whenever a log write is swallowed — which
/// <c>PlaybackService.LogActivityAsync</c> deliberately does — and could not answer a
/// question about a time window at all.
/// </para>
/// </remarks>
/// <param name="UserName">
/// The name recorded in the log, useful only as a fallback: the current display name comes
/// from the guild directory, which is why that lives in the Web project and not here.
/// </param>
public readonly record struct TopUser(ulong UserId, string UserName, int Plays);

/// <param name="IsDeleted">
/// The sound is gone from the library but its plays survive: the log holds no foreign key to
/// <c>Sound</c> precisely so history outlives a deletion anyone is allowed to perform.
/// </param>
public readonly record struct TopSound(
    Guid SoundId, string SoundName, string Emoji, bool IsDeleted, int Plays, DateTimeOffset? LastPlayedAt);

/// <param name="Day">A server-local day. This guild is effectively one timezone, and a
/// browser offset would cost a JS round trip on every load.</param>
public readonly record struct PlaysOnDay(DateOnly Day, int Plays);

/// <param name="TopPlayer">Null when nobody has pressed it.</param>
public readonly record struct SoundPlayDetail(
    Guid SoundId,
    int Plays,
    int PlaysThisWeek,
    DateTimeOffset? FirstPlayedAt,
    DateTimeOffset? LastPlayedAt,
    TopUser? TopPlayer);

public sealed class PlayStatistics(SoundboardDbContext db, TimeProvider? timeProvider = null)
{
    public const int TrendingWindowDays = 7;
    public const int TrendingMinimumPlays = 5;

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Plays per sound, keyed by id. A sound nobody has ever pressed is absent rather than
    /// present with a zero, so callers treat a missing key as "never played".
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, SoundPlayStats>> GetPerSoundAsync(
        CancellationToken cancellationToken = default) =>
        await BuildPerSoundQuery(_clock.GetUtcNow())
            .ToDictionaryAsync(stats => stats.SoundId, cancellationToken);

    /// <summary>
    /// Everything the detail dialog shows about one sound. Returns an all-zero result rather
    /// than null when nobody has pressed it, so the caller renders "never played" instead of
    /// branching.
    /// </summary>
    public async Task<SoundPlayDetail> GetForSoundAsync(
        Guid soundId, CancellationToken cancellationToken = default)
    {
        var weekAgo = _clock.GetUtcNow().AddDays(-TrendingWindowDays);

        var plays = db.ActivityLog.AsNoTracking()
            .Where(entry => entry.Kind == SoundboardActivity.Played && entry.SoundId == soundId);

        var totals = await plays
            .GroupBy(entry => entry.SoundId)
            .Select(group => new
            {
                Plays = group.Count(),
                ThisWeek = group.Count(entry => entry.OccurredAt >= weekAgo),
                First = (DateTimeOffset?)group.Min(entry => entry.OccurredAt),
                Last = (DateTimeOffset?)group.Max(entry => entry.OccurredAt),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (totals is null)
        {
            return new SoundPlayDetail(soundId, 0, 0, null, null, null);
        }

        // A second query rather than one clever one: the top presser groups by a different
        // key, and the log is indexed for exactly this filter.
        var top = await plays
            .Where(entry => entry.UserId != null)
            .GroupBy(entry => entry.UserId!.Value)
            .Select(group => new
            {
                UserId = group.Key,
                UserName = group.Max(entry => entry.UserName) ?? string.Empty,
                Plays = group.Count(),
            })
            .OrderByDescending(user => user.Plays)
            .FirstOrDefaultAsync(cancellationToken);

        return new SoundPlayDetail(
            soundId,
            totals.Plays,
            totals.ThisWeek,
            totals.First,
            totals.Last,
            top is null ? null : new TopUser(top.UserId, top.UserName, top.Plays));
    }

    public Task<int> GetTotalPlaysAsync(CancellationToken cancellationToken = default) =>
        db.ActivityLog.AsNoTracking()
            .CountAsync(entry => entry.Kind == SoundboardActivity.Played, cancellationToken);

    /// <summary>
    /// The most-pressed sounds, deleted ones included and flagged.
    /// </summary>
    public async Task<IReadOnlyList<TopSound>> GetTopSoundsAsync(
        int count, CancellationToken cancellationToken = default)
    {
        // Grouped by id, never by the denormalized name: anyone can rename a sound, and
        // grouping by name would split one clip into two rows the moment they did.
        var ranked = await db.ActivityLog.AsNoTracking()
            .Where(entry => entry.Kind == SoundboardActivity.Played && entry.SoundId != null)
            .GroupBy(entry => entry.SoundId!.Value)
            .Select(group => new
            {
                SoundId = group.Key,
                Plays = group.Count(),
                LastPlayedAt = (DateTimeOffset?)group.Max(entry => entry.OccurredAt),

                // Only used when the sound itself is gone; Max picks the most recent
                // spelling deterministically rather than an arbitrary row.
                LoggedName = group.Max(entry => entry.SoundName),
            })
            .OrderByDescending(row => row.Plays)
            .Take(count)
            .ToListAsync(cancellationToken);

        var ids = ranked.Select(row => row.SoundId).ToList();

        var live = await db.Sounds.AsNoTracking()
            .Where(sound => ids.Contains(sound.Id))
            .ToDictionaryAsync(sound => sound.Id, cancellationToken);

        return
        [
            .. ranked.Select(row => live.TryGetValue(row.SoundId, out var sound)
                ? new TopSound(row.SoundId, sound.Name, sound.Emoji, false, row.Plays, row.LastPlayedAt)
                : new TopSound(
                    row.SoundId,
                    string.IsNullOrWhiteSpace(row.LoggedName) ? "(deleted sound)" : row.LoggedName,
                    string.Empty,
                    true,
                    row.Plays,
                    row.LastPlayedAt))
        ];
    }

    /// <summary>Who presses the most buttons.</summary>
    public async Task<IReadOnlyList<TopUser>> GetTopUsersAsync(
        int count, CancellationToken cancellationToken = default) =>
        [
            .. (await db.ActivityLog.AsNoTracking()
                // The Kind filter already excludes them, but automatic departures are the
                // rows with no user at all and a "nobody" bucket would be nonsense.
                .Where(entry => entry.Kind == SoundboardActivity.Played && entry.UserId != null)
                .GroupBy(entry => entry.UserId!.Value)
                .Select(group => new
                {
                    UserId = group.Key,
                    UserName = group.Max(entry => entry.UserName),
                    Plays = group.Count(),
                })
                .OrderByDescending(row => row.Plays)
                .Take(count)
                .ToListAsync(cancellationToken))
            .Select(row => new TopUser(row.UserId, row.UserName ?? string.Empty, row.Plays))
        ];

    /// <summary>
    /// Plays per day over the last <paramref name="days"/> days, quiet days included as zero
    /// so a chart has no holes in it.
    /// </summary>
    /// <remarks>
    /// The bucketing happens in memory, deliberately: SQLite cannot group UTC ticks into
    /// days. Unlike the aggregates above, the window clause bounds this to the plays inside
    /// it rather than the whole log — which is what makes reading rows acceptable here and
    /// not elsewhere.
    /// </remarks>
    public async Task<IReadOnlyList<PlaysOnDay>> GetPlaysPerDayAsync(
        int days, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var firstDay = today.AddDays(-(days - 1));
        var since = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), _clock.GetLocalNow().Offset);

        var stamps = await db.ActivityLog.AsNoTracking()
            .Where(entry => entry.Kind == SoundboardActivity.Played && entry.OccurredAt >= since)
            .Select(entry => entry.OccurredAt)
            .ToListAsync(cancellationToken);

        var counts = stamps
            .GroupBy(stamp => DateOnly.FromDateTime(stamp.ToLocalTime().DateTime))
            .ToDictionary(group => group.Key, group => group.Count());

        return
        [
            .. Enumerable.Range(0, days)
                .Select(offset => firstDay.AddDays(offset))
                .Select(day => new PlaysOnDay(day, counts.GetValueOrDefault(day)))
        ];
    }

    /// <summary>
    /// Exposed so a test can assert on the generated SQL. A correct result says nothing about
    /// whether the work happened in the database or by dragging the log into memory.
    /// </summary>
    internal IQueryable<SoundPlayStats> BuildPerSoundQuery(DateTimeOffset now)
    {
        var weekAgo = now.AddDays(-TrendingWindowDays);
        var twoWeeksAgo = now.AddDays(-TrendingWindowDays * 2);

        // OccurredAt goes through UtcTicksConverter, so both the window comparisons and the
        // Max round-trip through it. That is the part worth pinning with a test rather than
        // assuming: a converter applied one way only yields year-0001 dates, not an error.
        return db.ActivityLog.AsNoTracking()
            .Where(entry => entry.Kind == SoundboardActivity.Played && entry.SoundId != null)
            .GroupBy(entry => entry.SoundId!.Value)
            .Select(group => new SoundPlayStats(
                group.Key,
                group.Count(),
                group.Count(entry => entry.OccurredAt >= weekAgo),
                group.Count(entry => entry.OccurredAt >= twoWeeksAgo && entry.OccurredAt < weekAgo),
                (DateTimeOffset?)group.Max(entry => entry.OccurredAt)));
    }
}
