using FourDotnet.BoogaBooster.DigitalTwin.Domain;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

public sealed class LightingModeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void Off_targets_darkness_whatever_the_intensity(double intensity)
    {
        Assert.Equal(0d, LightingMode.Off.TargetLevel(intensity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void Glow_targets_the_glow_fraction_whatever_the_intensity(double intensity)
    {
        Assert.Equal(RideParameters.LightGlowFraction, LightingMode.Glow.TargetLevel(intensity));
    }

    [Theory]
    [InlineData(0, 0.15)]
    [InlineData(0.5, 0.575)]
    [InlineData(1, 1)]
    public void Reactive_adds_the_scaled_intensity_on_top_of_the_glow(double intensity, double expected)
    {
        Assert.Equal(expected, LightingMode.Reactive.TargetLevel(intensity), 12);
    }

    [Theory]
    [InlineData(-0.5, 0.15)]
    [InlineData(1.5, 1)]
    public void Reactive_never_targets_outside_the_glow_and_full_output(double intensity, double expected)
    {
        Assert.Equal(expected, LightingMode.Reactive.TargetLevel(intensity), 12);
    }

    [Fact]
    public void An_unknown_mode_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((LightingMode)99).TargetLevel(0.5d));
    }
}
