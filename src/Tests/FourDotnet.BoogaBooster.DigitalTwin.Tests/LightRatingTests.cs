using FourDotnet.BoogaBooster.Core;
using FourDotnet.BoogaBooster.DigitalTwin.Domain;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

public sealed class LightRatingTests
{
    [Theory]
    [InlineData(0.001)]
    [InlineData(1_000)]
    [InlineData(5_000)]
    public void Accepts_a_positive_finite_rating(double lumens)
    {
        var rating = new LightRating(lumens);

        Assert.Equal(lumens, rating.RatedLumens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Rejects_a_rating_that_is_not_positive_and_finite(double lumens)
    {
        Assert.Throws<DomainValidationException>(() => new LightRating(lumens));
    }

    [Fact]
    public void Glow_is_fifteen_percent_of_the_rating()
    {
        Assert.Equal(150d, LightRating.Gondola.GlowLumens, 9);
        Assert.Equal(750d, LightRating.MillCrown.GlowLumens, 9);
    }

    [Fact]
    public void Ratings_per_part_type_match_the_specification()
    {
        Assert.Equal(5_000d, LightRating.MillCrown.RatedLumens);
        Assert.Equal(4_000d, LightRating.MillArm.RatedLumens);
        Assert.Equal(2_500d, LightRating.Hub.RatedLumens);
        Assert.Equal(2_500d, LightRating.HubArm.RatedLumens);
        Assert.Equal(1_000d, LightRating.Gondola.RatedLumens);
    }

    [Fact]
    public void The_rig_of_41_fixtures_is_rated_87_000_lumens()
    {
        var total = LightRating.MillCrown.RatedLumens
            + (RideParameters.HubCount * LightRating.MillArm.RatedLumens)
            + (RideParameters.HubCount * LightRating.Hub.RatedLumens)
            + (RideParameters.HubCount * RideParameters.GondolasPerHub
                * (LightRating.HubArm.RatedLumens + LightRating.Gondola.RatedLumens));

        Assert.Equal(87_000d, total);
    }

    [Fact]
    public void Ratings_with_the_same_lumens_are_equal()
    {
        Assert.Equal(new LightRating(2_500d), LightRating.Hub);
    }
}
