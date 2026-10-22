using FourDotnet.BoogaBooster.Core;
using FourDotnet.BoogaBooster.DigitalTwin.Domain;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

public sealed class LightTests
{
    private static readonly double Dt = TestHelpers.Dt.TotalSeconds;

    [Fact]
    public void A_new_light_is_dark()
    {
        var light = new Light(LightRating.Gondola);

        Assert.Equal(0d, light.Level);
        Assert.Equal(0d, light.Lumens);
        Assert.Same(LightRating.Gondola, light.Rating);
    }

    [Fact]
    public void Lumens_are_the_level_times_the_rating()
    {
        var light = new Light(LightRating.MillCrown);

        Step(light, 1d, seconds: 0.25d);

        Assert.Equal(light.Level * 5_000d, light.Lumens, 9);
    }

    [Fact]
    public void A_step_moves_part_of_the_way_and_never_jumps()
    {
        var light = new Light(LightRating.Gondola);

        light.Advance(1d, Dt);

        Assert.InRange(light.Level, 0.01d, 0.99d);
    }

    [Fact]
    public void Rising_follows_the_attack_time_constant()
    {
        var light = new Light(LightRating.Gondola);

        // After one time constant a first-order step covers 1 − e⁻¹ ≈ 63.2 %. The
        // elapsed time is taken from the steps actually taken (TimeSpan ticks round dt).
        var elapsed = Step(light, 1d, seconds: RideParameters.LightAttackSeconds);

        Assert.Equal(1d - Math.Exp(-elapsed / RideParameters.LightAttackSeconds), light.Level, 9);
    }

    [Fact]
    public void Falling_follows_the_decay_time_constant()
    {
        var light = Settled(1d);

        var elapsed = Step(light, 0d, seconds: RideParameters.LightDecaySeconds);

        Assert.Equal(Math.Exp(-elapsed / RideParameters.LightDecaySeconds), light.Level, 9);
    }

    [Fact]
    public void Rising_is_faster_than_falling_by_the_same_amount()
    {
        var rising = Settled(0d);
        var falling = Settled(1d);

        var riseTime = SecondsToCover(rising, target: 1d, until: level => level >= 0.9d);
        var fallTime = SecondsToCover(falling, target: 0d, until: level => level <= 0.1d);

        Assert.True(riseTime < fallTime, $"Rise took {riseTime} s, fall took {fallTime} s.");
    }

    [Theory]
    [InlineData(0d, 1d)]
    [InlineData(1d, 0d)]
    [InlineData(0.15d, 1d)]
    [InlineData(1d, 0.15d)]
    public void The_level_approaches_the_target_without_overshooting(double start, double target)
    {
        var light = Settled(start);
        var rising = target > start;

        for (var i = 0; i < 2_000; i++)
        {
            light.Advance(target, Dt);

            Assert.True(
                rising ? light.Level <= target : light.Level >= target,
                $"Level {light.Level} overshot target {target} at step {i}.");
        }

        Assert.Equal(target, light.Level, 6);
    }

    [Fact]
    public void Output_stays_within_zero_and_the_rating_under_any_target_sequence()
    {
        var light = new Light(LightRating.HubArm);
        var targets = new[] { 1d, 0d, 0.5d, 1d, 0.15d, 0d, 1d };

        foreach (var target in targets)
        {
            for (var i = 0; i < 200; i++)
            {
                light.Advance(target, Dt);
                Assert.InRange(light.Lumens, 0d, LightRating.HubArm.RatedLumens);
            }
        }
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rejects_a_target_outside_zero_to_one(double target)
    {
        var light = new Light(LightRating.Gondola);

        Assert.Throws<DomainValidationException>(() => light.Advance(target, Dt));
        Assert.Equal(0d, light.Level);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rejects_a_step_that_is_not_positive_and_finite(double dt)
    {
        var light = new Light(LightRating.Gondola);

        Assert.Throws<DomainValidationException>(() => light.Advance(1d, dt));
        Assert.Equal(0d, light.Level);
    }

    [Fact]
    public void Requires_a_rating()
    {
        Assert.Throws<ArgumentNullException>(() => new Light(null!));
    }

    private static Light Settled(double level)
    {
        var light = new Light(LightRating.Gondola);
        if (level > 0d)
        {
            Step(light, level, seconds: 5d);
        }

        return light;
    }

    private static double Step(Light light, double target, double seconds)
    {
        var steps = (int)Math.Round(seconds / Dt);
        for (var i = 0; i < steps; i++)
        {
            light.Advance(target, Dt);
        }

        return steps * Dt;
    }

    private static double SecondsToCover(Light light, double target, Func<double, bool> until)
    {
        var steps = 0;
        while (!until(light.Level))
        {
            light.Advance(target, Dt);
            steps++;
        }

        return steps * Dt;
    }
}
