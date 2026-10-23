using FourDotnet.BoogaBooster.DigitalTwin.Domain;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

/// <summary>
/// The light rig on the mill → hub → gondola tree (<c>docs/06-ride-lighting.md</c>
/// §6.1, §6.3): 41 fixtures, intensity propagated up by maximum.
/// </summary>
public sealed class LightRigTests
{
    private const double SettleSeconds = 15d;
    private const double LumenTolerance = 0.01d;

    [Fact]
    public void The_rig_has_41_distinct_fixtures_with_their_ratings()
    {
        var mill = new GreatMill();
        var fixtures = AllFixtures(mill);

        Assert.Equal(41, fixtures.Count);
        Assert.Equal(41, fixtures.Distinct().Count());

        Assert.Same(LightRating.MillCrown, mill.CrownLight.Rating);
        Assert.Equal(4, mill.ArmLights.Count);
        Assert.All(mill.ArmLights, light => Assert.Same(LightRating.MillArm, light.Rating));
        Assert.All(mill.Hubs, hub =>
        {
            Assert.Same(LightRating.Hub, hub.Light.Rating);
            Assert.Equal(4, hub.ArmLights.Count);
            Assert.All(hub.ArmLights, light => Assert.Same(LightRating.HubArm, light.Rating));
            Assert.All(hub.Gondolas, gondola => Assert.Same(LightRating.Gondola, gondola.Light.Rating));
        });

        Assert.Equal(87_000d, fixtures.Sum(light => light.Rating.RatedLumens));
    }

    [Fact]
    public void One_hard_hit_gondola_lights_its_hub_and_mill_arm()
    {
        var mill = new GreatMill();
        var hub = mill.GetHub(1);
        SetIntensity(hub.GetGondola(2), 0.9d);
        SetIntensity(hub.GetGondola(0), 0.2d);
        SetIntensity(hub.GetGondola(1), 0.2d);
        SetIntensity(hub.GetGondola(3), 0.2d);

        Assert.Equal(0.9d, hub.Intensity, 12);

        Settle(mill, LightingMode.Reactive);

        AssertSettledAt(hub.Light, 0.9d);
        AssertSettledAt(mill.ArmLights[1], 0.9d);
        AssertSettledAt(hub.ArmLights[2], 0.9d);
        AssertSettledAt(hub.ArmLights[0], 0.2d);
        AssertSettledAt(hub.ArmLights[1], 0.2d);
        AssertSettledAt(hub.ArmLights[3], 0.2d);
        AssertSettledAt(hub.GetGondola(2).Light, 0.9d);
        AssertSettledAt(hub.GetGondola(0).Light, 0.2d);
    }

    [Fact]
    public void The_crown_follows_the_hardest_hit_gondola_on_the_ride()
    {
        var mill = new GreatMill();
        SetIntensity(mill.GetHub(0).GetGondola(1), 0.3d);
        SetIntensity(mill.GetHub(2).GetGondola(3), 0.7d);
        SetIntensity(mill.GetHub(3).GetGondola(0), 0.5d);

        Assert.Equal(0.7d, mill.Intensity, 12);

        Settle(mill, LightingMode.Reactive);

        AssertSettledAt(mill.CrownLight, 0.7d);
        AssertSettledAt(mill.ArmLights[2], 0.7d);
        AssertSettledAt(mill.ArmLights[0], 0.3d);
        AssertSettledAt(mill.ArmLights[1], 0d);
    }

    [Fact]
    public void A_running_gondola_at_half_intensity_settles_at_575_lumens()
    {
        var mill = new GreatMill();
        var gondola = mill.GetHub(0).GetGondola(0);
        SetIntensity(gondola, 0.5d);

        Settle(mill, LightingMode.Reactive);

        Assert.Equal(575d, gondola.Light.Lumens, LumenTolerance);
    }

    [Fact]
    public void Glow_ignores_intensity_and_settles_every_fixture_at_fifteen_percent()
    {
        var mill = new GreatMill();
        SetIntensity(mill.GetHub(0).GetGondola(0), 1d);

        Settle(mill, LightingMode.Glow);

        Assert.All(AllFixtures(mill), light =>
            Assert.Equal(light.Rating.GlowLumens, light.Lumens, LumenTolerance));
        Assert.Equal(750d, mill.CrownLight.Lumens, LumenTolerance);
        Assert.Equal(150d, mill.GetHub(3).GetGondola(3).Light.Lumens, LumenTolerance);
    }

    [Fact]
    public void Off_settles_every_fixture_dark()
    {
        var mill = new GreatMill();
        SetIntensity(mill.GetHub(1).GetGondola(1), 1d);
        Settle(mill, LightingMode.Reactive);

        Settle(mill, LightingMode.Off);

        Assert.All(AllFixtures(mill), light => Assert.Equal(0d, light.Lumens, LumenTolerance));
    }

    [Fact]
    public void Every_fixture_stays_within_its_rating()
    {
        var mill = new GreatMill();
        SetIntensity(mill.GetHub(0).GetGondola(0), 1d);
        var dt = TestHelpers.Dt.TotalSeconds;

        foreach (var mode in new[] { LightingMode.Reactive, LightingMode.Glow, LightingMode.Off, LightingMode.Reactive })
        {
            for (var i = 0; i < 240; i++)
            {
                mill.AdvanceLights(mode, dt);
                Assert.All(AllFixtures(mill), light => Assert.InRange(light.Lumens, 0d, light.Rating.RatedLumens));
            }
        }
    }

    internal static List<Light> AllFixtures(GreatMill mill)
    {
        var fixtures = new List<Light> { mill.CrownLight };
        fixtures.AddRange(mill.ArmLights);
        foreach (var hub in mill.Hubs)
        {
            fixtures.Add(hub.Light);
            fixtures.AddRange(hub.ArmLights);
            fixtures.AddRange(hub.Gondolas.Select(gondola => gondola.Light));
        }

        return fixtures;
    }

    /// <summary>Gives a gondola the felt forward G that produces <paramref name="intensity"/>.</summary>
    private static void SetIntensity(Gondola gondola, double intensity) =>
        gondola.SetGForces(forwardG: intensity * RideParameters.MaxGForce, lateralG: 0d);

    private static void Settle(GreatMill mill, LightingMode mode)
    {
        var dt = TestHelpers.Dt.TotalSeconds;
        var steps = (int)Math.Round(SettleSeconds / dt);
        for (var i = 0; i < steps; i++)
        {
            mill.AdvanceLights(mode, dt);
        }
    }

    private static void AssertSettledAt(Light light, double intensity) =>
        Assert.Equal(
            LightingMode.Reactive.TargetLevel(intensity) * light.Rating.RatedLumens,
            light.Lumens,
            LumenTolerance);
}
