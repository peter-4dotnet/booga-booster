using FourDotnet.BoogaBooster.Core;
using FourDotnet.BoogaBooster.DigitalTwin.Domain;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

public sealed class GondolaIntensityTests
{
    [Fact]
    public void A_gondola_at_rest_has_no_intensity()
    {
        var gondola = new Gondola(0, 0);

        Assert.Equal(0d, gondola.Intensity);
    }

    [Fact]
    public void Half_the_allowed_g_gives_half_intensity()
    {
        var gondola = new Gondola(0, 0);

        gondola.SetGForces(forwardG: RideParameters.MaxGForce / 2d, lateralG: 0d);

        Assert.Equal(0.5d, gondola.Intensity, 12);
    }

    [Theory]
    [InlineData(4.5, 0)]
    [InlineData(6, 0)]
    [InlineData(0, -9)]
    [InlineData(4, 4)]
    public void At_or_beyond_the_allowed_g_the_intensity_is_one(double forwardG, double lateralG)
    {
        var gondola = new Gondola(0, 0);

        gondola.SetGForces(forwardG, lateralG);

        Assert.Equal(1d, gondola.Intensity, 12);
    }

    [Fact]
    public void Forward_and_lateral_g_combine_as_a_vector()
    {
        var gondola = new Gondola(0, 0);

        gondola.SetGForces(forwardG: 0.6d, lateralG: 0.8d);

        Assert.Equal(1.0d / RideParameters.MaxGForce, gondola.Intensity, 12);
    }

    [Fact]
    public void The_sign_of_the_g_force_does_not_matter()
    {
        var gondola = new Gondola(0, 0);

        gondola.SetGForces(forwardG: -0.6d, lateralG: -0.8d);

        Assert.Equal(1.0d / RideParameters.MaxGForce, gondola.Intensity, 12);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void Rejects_g_forces_that_are_not_finite(double forwardG, double lateralG)
    {
        var gondola = new Gondola(0, 0);

        Assert.Throws<DomainValidationException>(() => gondola.SetGForces(forwardG, lateralG));
    }

    [Fact]
    public void Intensity_follows_the_g_force_the_physics_computes()
    {
        var gondola = new Gondola(0, 0);
        gondola.ReleaseBrake();

        gondola.AdvancePhysics(millAngle: 0d, millOmega: 1.5d, hubAngle: 0d, hubOmega: 0d, dt: TestHelpers.Dt.TotalSeconds);

        var expected = Math.Min(1d, Math.Sqrt((gondola.ForwardG * gondola.ForwardG) + (gondola.LateralG * gondola.LateralG)) / RideParameters.MaxGForce);
        Assert.True(gondola.Intensity > 0d);
        Assert.Equal(expected, gondola.Intensity, 12);
    }

    [Fact]
    public void Each_gondola_carries_a_dark_gondola_rated_light()
    {
        var gondola = new Gondola(0, 0);

        Assert.Same(LightRating.Gondola, gondola.Light.Rating);
        Assert.Equal(0d, gondola.Light.Lumens);
    }
}
