using System.Threading;
using System.Threading.Tasks;
using FourDotnet.BoogaBooster.Core;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;
using FourDotnet.BoogaBooster.DigitalTwin.Application;
using FourDotnet.BoogaBooster.DigitalTwin.Domain;
using FourDotnet.BoogaBooster.DigitalTwin.Features.BoardPassenger;
using FourDotnet.BoogaBooster.DigitalTwin.Features.BrakeEngines;
using FourDotnet.BoogaBooster.DigitalTwin.Features.GetRideTelemetry;
using FourDotnet.BoogaBooster.DigitalTwin.Features.RequestRideStateTransition;
using FourDotnet.BoogaBooster.DigitalTwin.Features.SetGondolaBrake;
using FourDotnet.BoogaBooster.DigitalTwin.Features.SetHubEnginePower;
using FourDotnet.BoogaBooster.DigitalTwin.Features.SetMainEnginePower;
using FourDotnet.BoogaBooster.DigitalTwin.Features.StartRide;
using FourDotnet.BoogaBooster.DigitalTwin.Features.StopRide;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

public sealed class TelemetryAndCommandTests
{
    private static RideStore NewStore() => new(new RandomRideEventSampler(seed: 1));

    [Fact]
    public void Telemetry_snapshot_has_the_full_ride_shape()
    {
        var telemetry = NewStore().GetTelemetry();

        Assert.Equal(RideParameters.HubCount, telemetry.Hubs.Count);
        Assert.Equal(RideParameters.HubCount * RideParameters.GondolasPerHub, telemetry.Gondolas.Count);
        Assert.All(telemetry.Gondolas, g => Assert.Equal(RideParameters.SeatsPerGondola, g.Seats.Count));
        Assert.Equal(RideState.Idle, telemetry.State);
        Assert.Equal(new[] { RideState.Loading }, telemetry.AvailableTransitions);
    }

    [Fact]
    public void Telemetry_counts_boarded_passengers_as_occupied_seats()
    {
        var store = NewStore();

        var telemetry = store.BoardGroup(new[]
        {
            new PassengerWeight(70d),
            new PassengerWeight(80d),
            new PassengerWeight(90d),
        });

        Assert.Equal(3, telemetry.BoardedPassengerCount);
        var occupied = telemetry.Gondolas.SelectMany(g => g.Seats).Count(s => s.IsOccupied);
        Assert.Equal(3, occupied);
    }

    [Fact]
    public void Empty_ride_reports_zero_boarded_passengers()
    {
        Assert.Equal(0, NewStore().GetTelemetry().BoardedPassengerCount);
    }

    [Fact]
    public void Seat_telemetry_reports_occupied_and_unsecured_then_secured_after_the_countdown()
    {
        var store = NewStore();
        store.BoardPassenger(0, 0, SeatPosition.Left, new PassengerWeight(75d));

        var seatWhileLoading = SeatOf(store.GetTelemetry());
        Assert.True(seatWhileLoading.IsOccupied);
        Assert.False(seatWhileLoading.IsSecured);
        Assert.NotEqual(RestraintState.Secured, seatWhileLoading.Restraint);

        // The maximum natural delay is 30 s; advance a little past it.
        var steps = (int)(31d / TestHelpers.Dt.TotalSeconds);
        for (var i = 0; i < steps; i++)
        {
            store.Advance(TestHelpers.Dt);
        }

        var seatAfter = SeatOf(store.GetTelemetry());
        Assert.True(seatAfter.IsOccupied);
        Assert.True(seatAfter.IsSecured);
        Assert.Equal(RestraintState.Secured, seatAfter.Restraint);
    }

    private static SeatTelemetry SeatOf(RideTelemetry telemetry) =>
        telemetry.Gondolas
            .Single(g => g is { HubIndex: 0, Index: 0 })
            .Seats.Single(s => s.Position == SeatPosition.Left);

    [Fact]
    public void Setting_main_power_emits_the_consumed_watts()
    {
        var telemetry = NewStore().SetMainEnginePower(new EnginePower(100));

        Assert.Equal(RideParameters.MillMaxPowerWatts, telemetry.Mill.PowerWatts, 6);
    }

    [Fact]
    public void Setting_hub_power_applies_to_every_hub()
    {
        var telemetry = NewStore().SetHubEnginePower(new EnginePower(50));

        Assert.All(telemetry.Hubs, h => Assert.Equal(RideParameters.HubMaxPowerWatts * 0.5d, h.PowerWatts, 6));
    }

    [Fact]
    public void Releasing_a_brake_through_the_store_is_reflected_in_telemetry()
    {
        var store = NewStore();

        var telemetry = store.SetGondolaBrake(2, 1, GondolaBrakeState.Released);

        var gondola = System.Linq.Enumerable.Single(telemetry.Gondolas, g => g is { HubIndex: 2, Index: 1 });
        Assert.Equal(GondolaBrakeState.Released, gondola.Brake);
    }

    [Fact]
    public async Task All_command_handlers_drive_the_store()
    {
        var store = NewStore();
        var token = CancellationToken.None;

        await new SetMainEnginePowerCommandHandler(store).HandleAsync(new SetMainEnginePowerCommand(80), token);
        await new SetHubEnginePowerCommandHandler(store).HandleAsync(new SetHubEnginePowerCommand(60), token);
        await new BoardPassengerCommandHandler(store).HandleAsync(
            new BoardPassengerCommand(0, 0, SeatPosition.Left, 90d), token);
        await new BoardPassengerCommandHandler(store).HandleAsync(
            new BoardPassengerCommand(0, 1, SeatPosition.Right, null), token);
        await new SetGondolaBrakeCommandHandler(store).HandleAsync(
            new SetGondolaBrakeCommand(0, 0, GondolaBrakeState.Released), token);

        var query = await new GetRideTelemetryQueryHandler(store).HandleAsync(new GetRideTelemetryQuery(), token);
        Assert.Equal(RideParameters.MillMaxPowerWatts * 0.8d, query.Mill.PowerWatts, 6);
        Assert.Equal(RideState.Loading, query.State);
    }

    [Fact]
    public async Task Start_and_stop_handlers_move_a_safe_ride_through_its_states()
    {
        var store = NewStore();
        var token = CancellationToken.None;

        // Walk the empty (and therefore safe) ride up to Safe through the state machine.
        var transitions = new RequestRideStateTransitionCommandHandler(store);
        await transitions.HandleAsync(new RequestRideStateTransitionCommand(RideState.Loading), token);
        await transitions.HandleAsync(new RequestRideStateTransitionCommand(RideState.Safe), token);

        await new StartRideCommandHandler(store).HandleAsync(new StartRideCommand(), token);
        Assert.Equal(RideState.Started, store.GetTelemetry().State);

        await new StopRideCommandHandler(store).HandleAsync(new StopRideCommand(), token);
        Assert.Equal(RideState.Stopping, store.GetTelemetry().State);
    }

    [Fact]
    public void Engaging_the_engine_brake_cuts_mill_and_hub_power_and_reports_engaged()
    {
        var store = NewStore();
        store.SetMainEnginePower(new EnginePower(80));
        store.SetHubEnginePower(new EnginePower(60));

        var telemetry = store.SetEngineBrakes(true);

        Assert.True(telemetry.BrakesEngaged);
        Assert.Equal(0d, telemetry.Mill.PowerWatts);
        Assert.All(telemetry.Hubs, h => Assert.Equal(0d, h.PowerWatts));
    }

    [Fact]
    public void Releasing_the_engine_brake_reports_released()
    {
        var store = NewStore();
        store.SetEngineBrakes(true);

        var telemetry = store.SetEngineBrakes(false);

        Assert.False(telemetry.BrakesEngaged);
    }

    [Fact]
    public async Task Brake_engines_handler_engages_and_releases_the_store_brake()
    {
        var store = NewStore();
        store.SetMainEnginePower(new EnginePower(80));

        await new BrakeEnginesCommandHandler(store).HandleAsync(new BrakeEnginesCommand(true), CancellationToken.None);

        Assert.True(store.GetTelemetry().BrakesEngaged);
        Assert.Equal(0d, store.GetTelemetry().Mill.PowerWatts);

        await new BrakeEnginesCommandHandler(store).HandleAsync(new BrakeEnginesCommand(false), CancellationToken.None);

        Assert.False(store.GetTelemetry().BrakesEngaged);
    }

    [Fact]
    public async Task Request_state_transition_handler_accepts_a_legal_transition()
    {
        var store = NewStore();
        var handler = new RequestRideStateTransitionCommandHandler(store);

        await handler.HandleAsync(new RequestRideStateTransitionCommand(RideState.Loading), CancellationToken.None);

        Assert.Equal(RideState.Loading, store.GetTelemetry().State);
    }

    [Fact]
    public async Task Request_state_transition_handler_rejects_an_illegal_transition_and_leaves_state_unchanged()
    {
        var store = NewStore();
        var handler = new RequestRideStateTransitionCommandHandler(store);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            handler.HandleAsync(new RequestRideStateTransitionCommand(RideState.Started), CancellationToken.None));

        Assert.Equal(RideState.Idle, store.GetTelemetry().State);
    }
}
