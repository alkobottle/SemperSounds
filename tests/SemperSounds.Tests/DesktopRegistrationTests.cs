using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SemperSounds.Web.Services;

namespace SemperSounds.Tests;

/// <summary>
/// Pins the registration shape of the desktop companion's services.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DesktopBroadcaster"/> only ever subscribes to events. Nothing resolves it, so a
/// plain <c>AddSingleton</c> leaves the container never building it — and the failure is
/// completely silent: the hub answers, sounds play, and every tray icon simply never updates
/// again. That is why it is also registered as a hosted service, exactly as
/// <c>EntrySoundCoordinator</c> is.
/// </para>
/// <para>
/// The subtler half is <em>how</em>. <c>AddHostedService&lt;DesktopBroadcaster&gt;()</c> would
/// also force construction — of a second instance. The hub would then register connections on
/// one object while the events fired on another, which fails in precisely the same silent way.
/// The factory form is the fix, so that is what is asserted here.
/// </para>
/// </remarks>
public class DesktopRegistrationTests
{
    private static IServiceCollection Registered()
    {
        var services = new ServiceCollection();
        services.AddDesktopCompanion();
        return services;
    }

    [Fact]
    public void TheBroadcaster_IsASingleton()
    {
        var descriptor = Assert.Single(Registered(), d => d.ServiceType == typeof(DesktopBroadcaster));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void TheBroadcaster_IsAlsoAHostedService()
    {
        Assert.Contains(Registered(), d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void TheHostedService_IsTheVerySameObjectAsTheSingleton()
    {
        // The whole point. Resolving rather than constructing is what keeps the object the
        // events are wired on and the object the hub talks to the same one.
        var hosted = Assert.Single(Registered(), d => d.ServiceType == typeof(IHostedService));

        Assert.Null(hosted.ImplementationType);
        Assert.NotNull(hosted.ImplementationFactory);

        var singleton = RuntimeHelpers.GetUninitializedObject(typeof(DesktopBroadcaster));

        Assert.Same(singleton, hosted.ImplementationFactory(new StubProvider(singleton)));
    }

    [Fact]
    public void ThePairingCodeStore_IsASingleton()
    {
        // Codes outlive the request that mints them: scoped would hand every request an empty
        // store and no pairing could ever complete.
        var descriptor = Assert.Single(Registered(), d => d.ServiceType == typeof(DeviceCodeStore));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void TheCodeStore_CanBeBuiltWithoutRegisteringATimeProvider()
    {
        // It takes an optional TimeProvider so tests can drive the clock, and the container
        // does not resolve TimeProvider by default. If it declined to honour the default
        // value, pairing would throw the first time anybody tried it.
        using var provider = Registered().BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<DeviceCodeStore>());
    }

    /// <summary>Hands back one prepared object for whatever is asked of it.</summary>
    private sealed class StubProvider(object instance) : IServiceProvider
    {
        public object GetService(Type serviceType) => instance;
    }
}
