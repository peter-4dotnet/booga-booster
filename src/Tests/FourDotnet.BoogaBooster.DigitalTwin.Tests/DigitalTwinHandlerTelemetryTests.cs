using System.Diagnostics;
using FourDotnet.BoogaBooster.Core.Observability;
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
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

/// <summary>
/// Covers the span attributes all eleven DigitalTwin handlers add on top of the
/// base-class instrumentation (ADR-0009), the boarding and transition counters, and
/// the requirement that no passenger's name reaches a span.
/// </summary>
public sealed class DigitalTwinHandlerTelemetryTests : IDisposable
{
    private const string TransitionCounter = "boogabooster.ride.state.transitions";
    private const string BoardedCounter = "boogabooster.ride.passengers.boarded";

    private const string StateTag = "ride.state";
    private const string StateRequestedTag = "ride.state.requested";
    private const string HubIndexTag = "ride.hub.index";
    private const string GondolaIndexTag = "ride.gondola.index";
    private const string SeatTag = "ride.seat";
    private const string EngineTag = "ride.engine";
    private const string PowerTag = "ride.engine.power_percent";
    private const string DirectionTag = "ride.engine.direction";
    private const string BrakeTag = "ride.brake.engaged";
    private const string BrakeBeforeTag = "ride.brake.engaged.before";
    private const string GondolaBrakeTag = "ride.gondola.brake";
    private const string BoardedTag = "ride.passengers.boarded";
    private const string MillRpmTag = "ride.mill.rpm";
    private const string WeightSuppliedTag = "ride.passenger.weight_supplied";

    private readonly TelemetryRecorder _telemetry = new();

    public void Dispose() => _telemetry.Dispose();

    [Fact]
    public async Task SetMainEnginePower_TagsTheMillDrive_AndTheThrottle()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new SetMainEnginePowerCommandHandler(store).HandleAsync(
                new SetMainEnginePowerCommand(60d),
                Ct);
        }

        var activity = _telemetry.Activity("SetMainEnginePower");
        Assert.Equal("main", activity.GetTagItem(EngineTag));
        Assert.Equal(60d, activity.GetTagItem(PowerTag));
    }

    [Fact]
    public async Task SetHubEnginePower_TagsTheHubDrive_SoItIsDistinguishableFromTheMill()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new SetHubEnginePowerCommandHandler(store).HandleAsync(
                new SetHubEnginePowerCommand(35d),
                Ct);
        }

        var activity = _telemetry.Activity("SetHubEnginePower");
        Assert.Equal("hub", activity.GetTagItem(EngineTag));
        Assert.Equal(35d, activity.GetTagItem(PowerTag));
    }

    [Fact]
    public async Task SetMainEngineDirection_TagsTheMillDrive_AndTheDirection()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new SetMainEngineDirectionCommandHandler(store).HandleAsync(
                new SetMainEngineDirectionCommand(MotorDirection.Reverse),
                Ct);
        }

        var activity = _telemetry.Activity("SetMainEngineDirection");
        Assert.Equal("main", activity.GetTagItem(EngineTag));
        Assert.Equal(nameof(MotorDirection.Reverse), activity.GetTagItem(DirectionTag));
    }

    [Fact]
    public async Task SetHubEngineDirection_TagsTheHubDrive_AndTheDirection()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new SetHubEngineDirectionCommandHandler(store).HandleAsync(
                new SetHubEngineDirectionCommand(MotorDirection.Reverse),
                Ct);
        }

        var activity = _telemetry.Activity("SetHubEngineDirection");
        Assert.Equal("hub", activity.GetTagItem(EngineTag));
        Assert.Equal(nameof(MotorDirection.Reverse), activity.GetTagItem(DirectionTag));
    }

    [Fact]
    public async Task BoardPassenger_TagsTheSeatAddressed_AndThatAWeightWasSupplied()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new BoardPassengerCommandHandler(store).HandleAsync(
                new BoardPassengerCommand(HubIndex: 2, GondolaIndex: 3, SeatPosition.Right, WeightKg: 82d),
                Ct);
        }

        var activity = _telemetry.Activity("BoardPassenger");
        Assert.Equal(2, activity.GetTagItem(HubIndexTag));
        Assert.Equal(3, activity.GetTagItem(GondolaIndexTag));
        Assert.Equal(nameof(SeatPosition.Right), activity.GetTagItem(SeatTag));
        Assert.Equal(true, activity.GetTagItem(WeightSuppliedTag));
    }

    [Fact]
    public async Task BoardPassenger_RecordsThatNoWeightWasSupplied_WhenOneIsDrawn()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new BoardPassengerCommandHandler(store).HandleAsync(
                new BoardPassengerCommand(HubIndex: 0, GondolaIndex: 0, SeatPosition.Left, WeightKg: null),
                Ct);
        }

        var activity = _telemetry.Activity("BoardPassenger");
        Assert.Equal(false, activity.GetTagItem(WeightSuppliedTag));
    }

    [Fact]
    public async Task BoardPassenger_PutsNoPersonalDataOnTheSpan()
    {
        const double Weight = 137.5d;

        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new BoardPassengerCommandHandler(store).HandleAsync(
                new BoardPassengerCommand(HubIndex: 1, GondolaIndex: 1, SeatPosition.Left, Weight),
                Ct);
        }

        var activity = _telemetry.Activity("BoardPassenger");

        // The command carries no name at all, and the weight is deliberately reduced to
        // a boolean — so neither the value nor anything derived from it lands on a tag.
        Assert.DoesNotContain(
            activity.TagObjects,
            tag => tag.Value is double value && value == Weight);
        Assert.DoesNotContain(
            activity.Tags,
            tag => tag.Value?.Contains(Weight.ToString("0.#"), StringComparison.Ordinal) == true);
        Assert.All(activity.TagObjects, tag => Assert.DoesNotContain("name", tag.Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BoardPassenger_CountsTheBoarding()
    {
        var store = NewStore();
        var before = _telemetry.Measurements(BoardedCounter).Count;

        await new BoardPassengerCommandHandler(store).HandleAsync(
            new BoardPassengerCommand(HubIndex: 0, GondolaIndex: 1, SeatPosition.Left, WeightKg: 70d),
            Ct);

        Assert.True(_telemetry.Measurements(BoardedCounter).Count > before);
    }

    [Fact]
    public async Task SetGondolaBrake_TagsTheGondolaAddressed_AndTheStateAskedFor()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new SetGondolaBrakeCommandHandler(store).HandleAsync(
                new SetGondolaBrakeCommand(HubIndex: 1, GondolaIndex: 2, GondolaBrakeState.Engaged),
                Ct);
        }

        var activity = _telemetry.Activity("SetGondolaBrake");
        Assert.Equal(1, activity.GetTagItem(HubIndexTag));
        Assert.Equal(2, activity.GetTagItem(GondolaIndexTag));
        Assert.Equal(nameof(GondolaBrakeState.Engaged), activity.GetTagItem(GondolaBrakeTag));
    }

    [Fact]
    public async Task BrakeEngines_TagsTheStateAskedFor_AndTheOneAlreadyInEffect()
    {
        var store = NewStore();
        var handler = new BrakeEnginesCommandHandler(store);

        // Engage once, then engage again: the second command changes nothing, and the
        // span has to show that rather than looking identical to the first.
        await handler.HandleAsync(new BrakeEnginesCommand(Engaged: true), Ct);

        using (_telemetry.Scope())
        {
            await handler.HandleAsync(new BrakeEnginesCommand(Engaged: true), Ct);
        }

        var activity = _telemetry.Activity("BrakeEngines");
        Assert.Equal(true, activity.GetTagItem(BrakeTag));
        Assert.Equal(true, activity.GetTagItem(BrakeBeforeTag));
    }

    [Fact]
    public async Task RequestRideStateTransition_TagsTheTargetAndTheStateItFound()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new RequestRideStateTransitionCommandHandler(store).HandleAsync(
                new RequestRideStateTransitionCommand(RideState.Loading),
                Ct);
        }

        var activity = _telemetry.Activity("RequestRideStateTransition");
        Assert.Equal(nameof(RideState.Loading), activity.GetTagItem(StateRequestedTag));
        Assert.Equal(nameof(RideState.Idle), activity.GetTagItem(StateTag));
    }

    [Fact]
    public async Task ARejectedTransition_KeepsItsAttributes_AndIsCountedAsRejected()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            // Idle → Started is illegal, so the domain guard refuses it.
            await Assert.ThrowsAnyAsync<Exception>(
                () => new RequestRideStateTransitionCommandHandler(store).HandleAsync(
                    new RequestRideStateTransitionCommand(RideState.Started),
                    Ct));
        }

        var activity = _telemetry.Activity("RequestRideStateTransition");
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains(activity.Events, activityEvent => activityEvent.Name == "exception");

        // The whole point: the attributes set before the store call survive the throw,
        // so a rejected transition says what was asked for and from where.
        Assert.Equal(nameof(RideState.Started), activity.GetTagItem(StateRequestedTag));
        Assert.Equal(nameof(RideState.Idle), activity.GetTagItem(StateTag));

        var measurement = _telemetry.Measurement(
            TransitionCounter,
            (StateRequestedTag, nameof(RideState.Started)),
            ("outcome", TelemetryOutcome.Rejected));
        Assert.Equal(1, measurement.Value);
    }

    [Fact]
    public async Task AnAcceptedTransition_IsCountedSeparatelyFromARejectedOne()
    {
        var store = NewStore();

        await new RequestRideStateTransitionCommandHandler(store).HandleAsync(
            new RequestRideStateTransitionCommand(RideState.Loading),
            Ct);

        var measurement = _telemetry.Measurement(
            TransitionCounter,
            (StateRequestedTag, nameof(RideState.Loading)),
            ("outcome", TelemetryOutcome.Accepted));
        Assert.Equal(1, measurement.Value);

        // Both tags come from fixed sets — seven states, two outcomes — so the counter
        // stays aggregatable (design D6).
        Assert.Equal([StateRequestedTag, "outcome"], measurement.Tags.Keys);
    }

    [Fact]
    public async Task StartRide_TagsTheLifecycleStateItFound_ThoughItCarriesNoPayload()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            // Starting from Idle is refused, which is precisely the case where knowing
            // the state the command found is what explains the refusal (design D2).
            await Assert.ThrowsAnyAsync<Exception>(
                () => new StartRideCommandHandler(store).HandleAsync(new StartRideCommand(), Ct));
        }

        var activity = _telemetry.Activity("StartRide");
        Assert.Equal(nameof(RideState.Idle), activity.GetTagItem(StateTag));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task StopRide_TagsTheLifecycleStateItFound_ThoughItCarriesNoPayload()
    {
        var store = NewStore();
        store.RequestStateTransition(RideState.Loading);

        using (_telemetry.Scope())
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => new StopRideCommandHandler(store).HandleAsync(new StopRideCommand(), Ct));
        }

        var activity = _telemetry.Activity("StopRide");
        Assert.Equal(nameof(RideState.Loading), activity.GetTagItem(StateTag));
    }

    [Fact]
    public async Task GetRideTelemetry_SummarisesTheSnapshot_WithoutCopyingItsPayload()
    {
        var store = NewStore();
        await new BoardPassengerCommandHandler(store).HandleAsync(
            new BoardPassengerCommand(HubIndex: 0, GondolaIndex: 0, SeatPosition.Left, WeightKg: 80d),
            Ct);

        RideTelemetry response;
        using (_telemetry.Scope())
        {
            response = await new GetRideTelemetryQueryHandler(store).HandleAsync(new GetRideTelemetryQuery(), Ct);
        }

        var activity = _telemetry.Activity("GetRideTelemetry");
        Assert.Equal(response.State.ToString(), activity.GetTagItem(StateTag));
        Assert.Equal(response.BoardedPassengerCount, activity.GetTagItem(BoardedTag));
        Assert.Equal(response.Mill.Rpm, activity.GetTagItem(MillRpmTag));

        // The sixteen gondolas and four hubs stay in the response, not on the span.
        Assert.Equal(16, response.Gondolas.Count);
        Assert.All(
            activity.TagObjects,
            tag => Assert.False(
                tag.Value is System.Collections.IEnumerable and not string,
                $"{tag.Key} carries a collection; span attributes must be scalars."));
    }

    [Fact]
    public async Task EveryHandlerAttributeName_IsPrefixedForTheModule()
    {
        var store = NewStore();

        using (_telemetry.Scope())
        {
            await new SetGondolaBrakeCommandHandler(store).HandleAsync(
                new SetGondolaBrakeCommand(HubIndex: 0, GondolaIndex: 0, GondolaBrakeState.Engaged),
                Ct);
        }

        // The convention: the module's own attributes are dot-delimited, lower-case and
        // prefixed "ride."; the base class contributes the "boogabooster." ones.
        var activity = _telemetry.Activity("SetGondolaBrake");
        Assert.All(
            activity.TagObjects.Select(tag => tag.Key),
            key => Assert.True(
                key.StartsWith("ride.", StringComparison.Ordinal)
                    || key.StartsWith("boogabooster.", StringComparison.Ordinal),
                $"'{key}' is neither a module nor a base-class attribute name."));
        Assert.All(
            activity.TagObjects.Select(tag => tag.Key),
            key => Assert.Equal(key.ToLowerInvariant(), key));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RideStore NewStore() => new(new RandomRideEventSampler(seed: 42));
}
