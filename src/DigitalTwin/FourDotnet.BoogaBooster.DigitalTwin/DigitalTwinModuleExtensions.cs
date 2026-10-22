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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FourDotnet.BoogaBooster.DigitalTwin;

/// <summary>
/// Composition entry point for the DigitalTwin module (ADR-0007). A host wires the
/// module up with <see cref="AddDigitalTwinModule"/> and
/// <see cref="Endpoints.DigitalTwinEndpoints.MapDigitalTwinEndpoints"/>.
/// </summary>
public static class DigitalTwinModuleExtensions
{
    /// <summary>
    /// Registers the DigitalTwin module's services: the ride store (also the
    /// cross-module telemetry provider), the deterministic event sampler, the
    /// feature handlers, and the hosted fixed-timestep simulation loop.
    /// </summary>
    public static IHostApplicationBuilder AddDigitalTwinModule(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services
            .AddOptions<DigitalTwinModuleOptions>()
            .Bind(builder.Configuration.GetSection(DigitalTwinModuleOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);

        // Simulation state: one shared RideStore serving both the writer/reader
        // interface and the cross-module telemetry provider.
        services.TryAddSingleton<IRideEventSampler, RandomRideEventSampler>();
        services.TryAddSingleton<RideStore>();
        services.TryAddSingleton<IRideStore>(sp => sp.GetRequiredService<RideStore>());
        services.TryAddSingleton<IRideTelemetryProvider>(sp => sp.GetRequiredService<RideStore>());

        // Drains the ride's queue into free seats while it is loading (pulls groups
        // through the Queue module's public contract, ADR-0004).
        services.TryAddSingleton<RideLoadingCoordinator>();

        // The SSE telemetry broadcast producer (samples the store at the telemetry rate).
        services.TryAddSingleton<RideTelemetryStream>();

        // Feature handlers (CQRS — ADR-0005/0006).
        services.AddScoped<IQueryHandler<GetRideTelemetryQuery, RideTelemetry>, GetRideTelemetryQueryHandler>();
        services.AddScoped<ICommandHandler<SetMainEnginePowerCommand>, SetMainEnginePowerCommandHandler>();
        services.AddScoped<ICommandHandler<SetHubEnginePowerCommand>, SetHubEnginePowerCommandHandler>();
        services.AddScoped<ICommandHandler<SetMainEngineDirectionCommand>, SetMainEngineDirectionCommandHandler>();
        services.AddScoped<ICommandHandler<SetHubEngineDirectionCommand>, SetHubEngineDirectionCommandHandler>();
        services.AddScoped<ICommandHandler<BoardPassengerCommand>, BoardPassengerCommandHandler>();
        services.AddScoped<ICommandHandler<SetGondolaBrakeCommand>, SetGondolaBrakeCommandHandler>();
        services.AddScoped<ICommandHandler<BrakeEnginesCommand>, BrakeEnginesCommandHandler>();
        services.AddScoped<ICommandHandler<StartRideCommand>, StartRideCommandHandler>();
        services.AddScoped<ICommandHandler<StopRideCommand>, StopRideCommandHandler>();
        services.AddScoped<ICommandHandler<RequestRideStateTransitionCommand>, RequestRideStateTransitionCommandHandler>();

        // The autonomous fixed-timestep simulation loop.
        services.AddHostedService<RideSimulationService>();

        return builder;
    }
}
