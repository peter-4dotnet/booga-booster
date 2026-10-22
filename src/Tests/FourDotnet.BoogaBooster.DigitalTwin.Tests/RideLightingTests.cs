using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Domain;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

/// <summary>
/// The light rig driven by the ride's tick and lifecycle (<c>docs/06-ride-lighting.md</c>
/// §6.4, §6.7). The settled reactive value at a held intensity (575 lm at 0.5) is in
/// <see cref="LightRigTests"/>, because a running ride recomputes G every tick.
/// </summary>
public sealed class RideLightingTests
{
    private const double LumenTolerance = 0.01d;

    [Theory]
    [InlineData(RideState.Idle, LightingMode.Off)]
    [InlineData(RideState.Loading, LightingMode.Glow)]
    [InlineData(RideState.Safe, LightingMode.Glow)]
    [InlineData(RideState.Offloading, LightingMode.Glow)]
    [InlineData(RideState.Started, LightingMode.Reactive)]
    [InlineData(RideState.Stopping, LightingMode.Reactive)]
    [InlineData(RideState.EmergencyStop, LightingMode.Reactive)]
    public void Each_lifecycle_state_selects_its_lighting_mode(RideState state, LightingMode expected)
    {
        Assert.Equal(expected, Ride.LightingModeFor(state));
    }

    [Fact]
    public void An_idle_ride_is_dark()
    {
        var ride = Ride.Create();

        RideSafetyTests.Advance(ride, seconds: 5d);

        Assert.All(LightRigTests.AllFixtures(ride.Mill), light => Assert.Equal(0d, light.Lumens));
    }

    [Fact]
    public void A_loading_ride_settles_at_its_glow()
    {
        var ride = Ride.Create();
        ride.RequestTransition(RideState.Loading);

        RideSafetyTests.Advance(ride, seconds: 15d);

        Assert.Equal(750d, ride.Mill.CrownLight.Lumens, LumenTolerance);
        Assert.All(ride.Mill.Hubs, hub =>
            Assert.All(hub.Gondolas, gondola => Assert.Equal(150d, gondola.Light.Lumens, LumenTolerance)));
        Assert.All(LightRigTests.AllFixtures(ride.Mill), light =>
            Assert.Equal(light.Rating.GlowLumens, light.Lumens, LumenTolerance));
    }

    [Fact]
    public void A_running_ride_shines_brighter_than_its_glow()
    {
        var ride = SpinningRide(seconds: 20d);

        Assert.True(ride.Mill.Intensity > 0d);
        Assert.True(
            ride.Mill.CrownLight.Lumens > LightRating.MillCrown.GlowLumens + 100d,
            $"Crown at {ride.Mill.CrownLight.Lumens} lm while running.");
    }

    [Fact]
    public void Every_fixture_follows_its_mode_and_intensity_through_a_whole_ride()
    {
        // A shadow fixture per gondola is driven with the mode the spec prescribes for
        // the state the ride is in after each tick, and the gondola's intensity that tick.
        // The ride's own lights must match it exactly — through loading, a run, an
        // emergency stop, offloading and the return to idle.
        var ride = Ride.Create();
        var shadows = Gondolas(ride).Select(_ => new Light(LightRating.Gondola)).ToArray();
        var crownShadow = new Light(LightRating.MillCrown);
        var visited = new HashSet<RideState>();

        void Tick()
        {
            ride.Advance(TestHelpers.Dt);
            var mode = ExpectedMode(ride.CurrentState);
            visited.Add(ride.CurrentState);

            var gondolas = Gondolas(ride);
            for (var k = 0; k < gondolas.Count; k++)
            {
                shadows[k].Advance(mode.TargetLevel(gondolas[k].Intensity), TestHelpers.Dt.TotalSeconds);
                Assert.Equal(shadows[k].Lumens, gondolas[k].Light.Lumens);
            }

            crownShadow.Advance(mode.TargetLevel(ride.Mill.Intensity), TestHelpers.Dt.TotalSeconds);
            Assert.Equal(crownShadow.Lumens, ride.Mill.CrownLight.Lumens);
        }

        ride.RequestTransition(RideState.Loading);
        Repeat(Tick, seconds: 1d);
        ride.RequestTransition(RideState.Safe);
        Repeat(Tick, seconds: 1d);
        ride.RequestTransition(RideState.Started);
        ride.SetMainEnginePower(new EnginePower(100d));
        ride.SetHubEnginePower(new EnginePower(100d));
        Repeat(Tick, seconds: 10d);
        ride.RequestTransition(RideState.EmergencyStop);
        for (var i = 0; i < 6_000 && ride.CurrentState != RideState.Idle; i++)
        {
            Tick();
        }

        Repeat(Tick, seconds: 1d);

        Assert.Equal(RideState.Idle, ride.CurrentState);
        Assert.Contains(RideState.Started, visited);
        Assert.Contains(RideState.EmergencyStop, visited);
    }

    [Fact]
    public void An_emergency_stop_keeps_the_rig_reacting_to_g()
    {
        var ride = SpinningRide(seconds: 20d);
        ride.RequestTransition(RideState.EmergencyStop);

        ride.Advance(TestHelpers.Dt);

        Assert.Equal(RideState.EmergencyStop, ride.CurrentState);
        Assert.True(ride.Mill.Intensity > 0d, "The braking ride still has G.");

        // The crown is still aiming above the glow: its reactive target, not the glow.
        var target = LightingMode.Reactive.TargetLevel(ride.Mill.Intensity);
        Assert.True(target > RideParameters.LightGlowFraction);
    }

    [Fact]
    public void Returning_to_idle_fades_the_rig_out_gradually()
    {
        var ride = SpinningRide(seconds: 20d);
        ride.RequestTransition(RideState.Stopping);
        while (ride.CurrentState != RideState.Idle)
        {
            ride.Advance(TestHelpers.Dt);
        }

        var atIdle = ride.Mill.CrownLight.Lumens;
        Assert.True(atIdle > 0d, "The crown is still lit when the ride goes idle.");

        ride.Advance(TestHelpers.Dt);
        var oneTickLater = ride.Mill.CrownLight.Lumens;
        Assert.InRange(oneTickLater, atIdle * 0.9d, atIdle);

        RideSafetyTests.Advance(ride, seconds: 15d);
        Assert.All(LightRigTests.AllFixtures(ride.Mill), light => Assert.Equal(0d, light.Lumens, LumenTolerance));
    }

    [Fact]
    public void Two_identical_rides_advanced_identically_report_identical_lights()
    {
        var first = Ride.Create();
        var second = Ride.Create();

        void Both(Action<Ride> action)
        {
            action(first);
            action(second);
        }

        void StepBoth(double seconds)
        {
            var steps = (int)(seconds / TestHelpers.Dt.TotalSeconds);
            for (var i = 0; i < steps; i++)
            {
                Both(ride => ride.Advance(TestHelpers.Dt));
                var a = LightRigTests.AllFixtures(first.Mill);
                var b = LightRigTests.AllFixtures(second.Mill);
                for (var k = 0; k < a.Count; k++)
                {
                    Assert.Equal(a[k].Lumens, b[k].Lumens);
                }
            }
        }

        Both(ride => RideSafetyTests.FillEverySeat(ride, kilograms: 80));
        StepBoth(1d);
        Both(ride => ride.RequestTransition(RideState.Safe));
        Both(ride => ride.RequestTransition(RideState.Started));
        Both(ride => ride.SetMainEnginePower(new EnginePower(80d)));
        Both(ride => ride.SetHubEnginePower(new EnginePower(60d)));
        StepBoth(8d);
        Both(ride => ride.RequestTransition(RideState.Stopping));
        StepBoth(4d);
    }

    private static Ride SpinningRide(double seconds)
    {
        var ride = Ride.Create();
        ride.RequestTransition(RideState.Loading);
        ride.RequestTransition(RideState.Safe);
        ride.RequestTransition(RideState.Started);
        ride.SetMainEnginePower(new EnginePower(100d));
        ride.SetHubEnginePower(new EnginePower(100d));
        RideSafetyTests.Advance(ride, seconds);
        return ride;
    }

    private static LightingMode ExpectedMode(RideState state) => state switch
    {
        RideState.Idle => LightingMode.Off,
        RideState.Loading or RideState.Safe or RideState.Offloading => LightingMode.Glow,
        _ => LightingMode.Reactive,
    };

    private static List<Gondola> Gondolas(Ride ride) =>
        ride.Mill.Hubs.SelectMany(hub => hub.Gondolas).ToList();

    private static void Repeat(Action tick, double seconds)
    {
        var steps = (int)(seconds / TestHelpers.Dt.TotalSeconds);
        for (var i = 0; i < steps; i++)
        {
            tick();
        }
    }
}
