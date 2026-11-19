using FamilyResilience.Application;
using FamilyResilience.Application.Safeguarding;
using FamilyResilience.Domain.Safeguarding;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.UnitTests.Application.Safeguarding;

public sealed class SafeguardingServicesTests
{
    [Fact]
    public void The_crisis_screen_is_built_while_services_are_registered_so_a_bad_list_fails_start_up()
    {
        var services = new ServiceCollection().AddApplication();

        var screen = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CrisisSignalScreen));
        Assert.Equal(ServiceLifetime.Singleton, screen.Lifetime);
        Assert.Equal(CrisisSignalCatalog.SupportedVersion, Assert.IsType<CrisisSignalScreen>(screen.ImplementationInstance).Version);
    }

    [Fact]
    public void The_recorder_is_scoped_and_its_metrics_are_one_per_host()
    {
        var services = new ServiceCollection().AddApplication();

        var recorder = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISafetySignalRecorder));
        Assert.Equal((ServiceLifetime.Scoped, typeof(RecordSafetySignal)), (recorder.Lifetime, recorder.ImplementationType));
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(SafeguardingMetrics)).Lifetime);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(RecordSafetySignal));
    }
}
