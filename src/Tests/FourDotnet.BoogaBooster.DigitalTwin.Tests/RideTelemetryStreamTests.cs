using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Application;
using FourDotnet.BoogaBooster.DigitalTwin.Domain;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

/// <summary>The SSE telemetry broadcast producer: cadence, running-gate, and cancellation.</summary>
public sealed class RideTelemetryStreamTests
{
    [Fact]
    public async Task It_emits_a_frame_per_telemetry_interval_while_running()
    {
        var store = RunningStore();
        var clock = new FakeTimeProvider();
        var stream = new RideTelemetryStream(store, clock);

        await using var frames = stream.Stream(CancellationToken.None).GetAsyncEnumerator();

        // The first frame is emitted immediately (before the first delay).
        Assert.True(await frames.MoveNextAsync());

        // Each subsequent frame arrives one telemetry interval later.
        var second = frames.MoveNextAsync();
        Assert.False(second.IsCompleted);
        clock.Advance(RideParameters.TelemetryInterval);
        Assert.True(await second);

        var third = frames.MoveNextAsync();
        Assert.False(third.IsCompleted);
        clock.Advance(RideParameters.TelemetryInterval);
        Assert.True(await third);
    }

    [Fact]
    public async Task It_emits_nothing_while_the_ride_is_idle_then_resumes_once_it_is_active()
    {
        var store = new RideStore(new RandomRideEventSampler(seed: 1)); // idle
        var clock = new FakeTimeProvider();
        var stream = new RideTelemetryStream(store, clock);

        await using var frames = stream.Stream(CancellationToken.None).GetAsyncEnumerator();

        // Idle: the loop keeps ticking but never yields a frame.
        var pending = frames.MoveNextAsync();
        Assert.False(pending.IsCompleted);
        clock.Advance(RideParameters.TelemetryInterval * 10);
        Assert.False(pending.IsCompleted);

        // Once the ride starts, the very next tick yields a frame.
        DriveToStarted(store);
        clock.Advance(RideParameters.TelemetryInterval);
        Assert.True(await pending);
        Assert.Equal(RideState.Started, frames.Current.State);
    }

    [Fact]
    public async Task It_emits_frames_while_the_ride_is_loading()
    {
        var store = new RideStore(new RandomRideEventSampler(seed: 1));
        store.BoardPassenger(0, 0, SeatPosition.Left, new PassengerWeight(75d)); // moves the ride into Loading
        var clock = new FakeTimeProvider();
        var stream = new RideTelemetryStream(store, clock);

        await using var frames = stream.Stream(CancellationToken.None).GetAsyncEnumerator();

        // Boarding happens during Loading, and the stream must show it — not stay silent
        // until the ride runs.
        Assert.True(await frames.MoveNextAsync());
        Assert.Equal(RideState.Loading, frames.Current.State);
        Assert.Equal(1, frames.Current.BoardedPassengerCount);
    }

    [Fact]
    public async Task It_shows_boarding_progress_across_successive_frames()
    {
        var store = new RideStore(new RandomRideEventSampler(seed: 1));
        store.BoardPassenger(0, 0, SeatPosition.Left, new PassengerWeight(75d));
        var clock = new FakeTimeProvider();
        var stream = new RideTelemetryStream(store, clock);

        await using var frames = stream.Stream(CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await frames.MoveNextAsync());
        Assert.Equal(1, frames.Current.BoardedPassengerCount);

        // A second passenger boards while the stream is live; the next frame reflects it.
        store.BoardPassenger(0, 1, SeatPosition.Right, new PassengerWeight(80d));
        var next = frames.MoveNextAsync();
        clock.Advance(RideParameters.TelemetryInterval);
        Assert.True(await next);
        Assert.Equal(2, frames.Current.BoardedPassengerCount);
    }

    [Fact]
    public async Task It_stops_promptly_when_the_client_disconnects()
    {
        var store = RunningStore();
        var clock = new FakeTimeProvider();
        var stream = new RideTelemetryStream(store, clock);
        using var cts = new CancellationTokenSource();

        await using var frames = stream.Stream(cts.Token).GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync()); // first frame

        var next = frames.MoveNextAsync(); // awaiting the next interval
        Assert.False(next.IsCompleted);

        await cts.CancelAsync();
        Assert.False(await next); // the stream ends gracefully
    }

    [Fact]
    public async Task Each_frame_is_a_full_ride_snapshot()
    {
        var store = RunningStore();
        var clock = new FakeTimeProvider();
        var stream = new RideTelemetryStream(store, clock);

        await using var frames = stream.Stream(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync());

        var frame = frames.Current;
        Assert.Equal(RideParameters.HubCount, frame.Hubs.Count);
        Assert.Equal(RideParameters.HubCount * RideParameters.GondolasPerHub, frame.Gondolas.Count);
        Assert.All(frame.Gondolas, g => Assert.Equal(RideParameters.SeatsPerGondola, g.Seats.Count));
        Assert.NotNull(frame.Mill);
    }

    [Fact]
    public async Task Each_frame_carries_the_output_of_all_41_light_fixtures()
    {
        var store = RunningStore();
        store.Advance(RideParameters.TimeStep);
        var clock = new FakeTimeProvider();
        var stream = new RideTelemetryStream(store, clock);

        await using var frames = stream.Stream(TestContext.Current.CancellationToken).GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync());

        var frame = frames.Current;
        var lumens = new List<double> { frame.Mill.CrownLightLumens };
        lumens.AddRange(frame.Mill.ArmLightLumens);
        foreach (var hub in frame.Hubs)
        {
            lumens.Add(hub.LightLumens);
            lumens.AddRange(hub.ArmLightLumens);
        }

        lumens.AddRange(frame.Gondolas.Select(g => g.LightLumens));

        Assert.Equal(41, lumens.Count);
        Assert.All(lumens, value => Assert.True(value > 0d, "A started ride's rig is lit."));
    }

    private static RideStore RunningStore()
    {
        var store = new RideStore(new RandomRideEventSampler(seed: 1));
        DriveToStarted(store);
        return store;
    }

    private static void DriveToStarted(RideStore store)
    {
        // The empty ride is safe, so it can walk straight up to Started.
        store.RequestStateTransition(RideState.Loading);
        store.RequestStateTransition(RideState.Safe);
        store.RequestStateTransition(RideState.Started);
    }
}
