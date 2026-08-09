using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SemperSounds.Core.Sounds;

namespace SemperSounds.Core.Data;

public sealed class SoundboardDbContext(DbContextOptions<SoundboardDbContext> options)
    : DbContext(options)
{
    /// <summary>
    /// SQLite has no DateTimeOffset type and refuses to ORDER BY one, which breaks any
    /// "newest first" query. Storing UTC ticks as an integer sorts correctly in SQL and
    /// indexes properly. Everything written here is already UTC, so the offset is not lost.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter =
        new(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    public DbSet<Sound> Sounds => Set<Sound>();
    public DbSet<ActivityLogEntry> ActivityLog => Set<ActivityLogEntry>();
    public DbSet<Favorite> Favorites => Set<Favorite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Sound>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).IsRequired().HasMaxLength(100);

            // Required, with a default so rows created before emoji existed stay valid.
            entity.Property(s => s.Emoji)
                .IsRequired()
                .HasMaxLength(SoundEmoji.MaxLength)
                .HasDefaultValue(SoundEmoji.DefaultEmoji);
            entity.Property(s => s.Tags).HasMaxLength(500);
            entity.Property(s => s.UploaderName).IsRequired().HasMaxLength(100);

            // SQLite has no unsigned 64-bit type, so Discord snowflakes round-trip
            // through long. Values above long.MaxValue are not reachable for snowflakes.
            entity.Property(s => s.UploaderId).HasConversion<long>();
            entity.Property(s => s.UploadedAt).HasConversion(UtcTicksConverter);

            entity.HasIndex(s => s.Name);
        });

        modelBuilder.Entity<ActivityLogEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasConversion<int>();
            entity.Property(e => e.SoundName).HasMaxLength(100);
            entity.Property(e => e.UserName).HasMaxLength(100);
            entity.Property(e => e.ChannelName).HasMaxLength(100);
            entity.Property(e => e.UserId).HasConversion<long?>();
            entity.Property(e => e.ChannelId).HasConversion<long>();
            entity.Property(e => e.OccurredAt).HasConversion(UtcTicksConverter);

            // The log is read newest-first and nothing else.
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<Favorite>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.UserId).HasConversion<long>();
            entity.Property(f => f.CreatedAt).HasConversion(UtcTicksConverter);

            // Cascade: anyone may delete a sound, and it should drop out of everybody's
            // favourites rather than linger as a dangling shortcut.
            entity.HasOne(f => f.Sound)
                .WithMany()
                .HasForeignKey(f => f.SoundId)
                .OnDelete(DeleteBehavior.Cascade);

            // Enforced in the schema, not just in code: a sound cannot be starred twice,
            // and a slot holds one sound.
            entity.HasIndex(f => new { f.UserId, f.SoundId }).IsUnique();
            entity.HasIndex(f => new { f.UserId, f.Slot }).IsUnique();
        });
    }
}
