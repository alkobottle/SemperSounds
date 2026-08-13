using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SemperSounds.Core.Data;
using SemperSounds.Core.EntrySounds;

namespace SemperSounds.Tests;

/// <summary>
/// Pins that the container can actually build the entry sound services.
/// </summary>
/// <remarks>
/// Both take an optional <see cref="TimeProvider"/> so tests can drive the clock, and the
/// dependency injection container does not resolve <c>TimeProvider</c> by default. If it
/// also refused to honour the default value, every one of these would throw the first time
/// a page touched it — a failure no unit test constructing them by hand would ever see.
/// </remarks>
public sealed class EntrySoundRegistrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public EntrySoundRegistrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class StubPermissions : IGuildPermissions
    {
        public bool? IsAdministrator(ulong userId) => false;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddDbContext<SoundboardDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<IGuildPermissions, StubPermissions>();
        services.AddScoped<EntrySoundLibrary>();
        services.AddScoped<EntrySoundAdmin>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void EntrySoundServices_CanBeResolvedWithoutRegisteringATimeProvider()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EntrySoundLibrary>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EntrySoundAdmin>());
    }

    [Fact]
    public async Task AResolvedAdminService_UsesTheRealClock()
    {
        // The default has to be TimeProvider.System rather than null, or the first snooze
        // would fail with a null reference instead of a refusal.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<SoundboardDbContext>();
        await db.Database.EnsureCreatedAsync();

        var admin = scope.ServiceProvider.GetRequiredService<EntrySoundAdmin>();
        var result = await admin.SnoozeAsync(1, TimeSpan.FromHours(1));

        // Refused because the stub says "not an administrator" — not thrown.
        Assert.False(result.IsSuccess);
    }
}
