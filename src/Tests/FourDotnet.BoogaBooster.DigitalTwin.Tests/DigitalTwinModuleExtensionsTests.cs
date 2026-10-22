using FourDotnet.BoogaBooster.Core.Cqrs;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;
using FourDotnet.BoogaBooster.DigitalTwin.Application;
using FourDotnet.BoogaBooster.DigitalTwin.Features.BoardPassenger;
using FourDotnet.BoogaBooster.DigitalTwin.Features.BrakeEngines;
using FourDotnet.BoogaBooster.DigitalTwin.Features.GetRideTelemetry;
using FourDotnet.BoogaBooster.DigitalTwin.Features.RequestRideStateTransition;
using FourDotnet.BoogaBooster.DigitalTwin.Features.SetGondolaBrake;
using FourDotnet.BoogaBooster.DigitalTwin.Features.SetHubEngineDirection;
using FourDotnet.BoogaBooster.DigitalTwin.Features.SetHubEnginePower;
using FourDotnet.BoogaBooster.DigitalTwin.Features.SetMainEngineDirection;
using FourDotnet.BoogaBooster.DigitalTwin.Features.SetMainEnginePower;
using FourDotnet.BoogaBooster.DigitalTwin.Features.StartRide;
using FourDotnet.BoogaBooster.DigitalTwin.Features.StopRide;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

/// <summary>
/// Verifies the DigitalTwin module composition entry point (ADR-0007): a single
/// <see cref="DigitalTwinModuleExtensions.AddDigitalTwinModule"/> call registers the
/// store, sampler, telemetry provider, feature handlers, and the hosted loop.
/// </summary>
public sealed class DigitalTwinModuleExtensionsTests
{
    private static ServiceProvider BuildProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddDigitalTwinModule();
        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    public void AddDigitalTwinModule_RegistersSimulationServices()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IRideEventSampler>());
        Assert.NotNull(provider.GetRequiredService<IRideStore>());
        Assert.NotNull(provider.GetRequiredService<IRideTelemetryProvider>());
        Assert.NotNull(provider.GetRequiredService<RideTelemetryStream>());
    }

    [Fact]
    public void AddDigitalTwinModule_SharesASingleRideStore()
    {
        using var provider = BuildProvider();

        // The loop (writer), the read query, and the cross-module provider must all
        // see the same in-memory ride.
        Assert.Same(
            provider.GetRequiredService<IRideStore>(),
            (IRideStore)provider.GetRequiredService<IRideTelemetryProvider>());
    }

    [Fact]
    public void AddDigitalTwinModule_RegistersFeatureHandlers()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IQueryHandler<GetRideTelemetryQuery, RideTelemetry>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<SetMainEnginePowerCommand>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<SetHubEnginePowerCommand>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<SetMainEngineDirectionCommand>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<SetHubEngineDirectionCommand>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<BoardPassengerCommand>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<SetGondolaBrakeCommand>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<BrakeEnginesCommand>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<StartRideCommand>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<StopRideCommand>>());
        Assert.NotNull(sp.GetRequiredService<ICommandHandler<RequestRideStateTransitionCommand>>());
    }

    [Fact]
    public void AddDigitalTwinModule_RegistersHostedSimulation()
    {
        using var provider = BuildProvider();

        Assert.Contains(provider.GetServices<IHostedService>(), s => s is RideSimulationService);
    }

    [Fact]
    public void AddDigitalTwinModule_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IHostApplicationBuilder)null!).AddDigitalTwinModule());
    }
}
