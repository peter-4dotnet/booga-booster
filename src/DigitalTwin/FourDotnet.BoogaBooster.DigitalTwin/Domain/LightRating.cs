using FourDotnet.BoogaBooster.Core;

namespace FourDotnet.BoogaBooster.DigitalTwin.Domain;

/// <summary>
/// A light fixture's rated (maximum) output in lumens, validated on construction
/// (ADR-0003). Its glow is the shared <see cref="RideParameters.LightGlowFraction"/>
/// of the rating (<c>docs/06-ride-lighting.md</c> §6.1).
/// </summary>
public sealed record LightRating
{
    /// <summary>The mill crown fixture's rating.</summary>
    public static readonly LightRating MillCrown = new(RideParameters.MillCrownLightRatedLumens);

    /// <summary>A mill arm fixture's rating.</summary>
    public static readonly LightRating MillArm = new(RideParameters.MillArmLightRatedLumens);

    /// <summary>A hub fixture's rating.</summary>
    public static readonly LightRating Hub = new(RideParameters.HubLightRatedLumens);

    /// <summary>A hub arm fixture's rating.</summary>
    public static readonly LightRating HubArm = new(RideParameters.HubArmLightRatedLumens);

    /// <summary>A gondola fixture's rating.</summary>
    public static readonly LightRating Gondola = new(RideParameters.GondolaLightRatedLumens);

    public LightRating(double ratedLumens)
    {
        if (double.IsNaN(ratedLumens) || double.IsInfinity(ratedLumens))
        {
            throw new DomainValidationException("A light rating must be a finite number of lumens.");
        }

        if (ratedLumens <= 0d)
        {
            throw new DomainValidationException("A light rating must be greater than 0 lm.");
        }

        RatedLumens = ratedLumens;
    }

    /// <summary>The fixture's maximum output (lm).</summary>
    public double RatedLumens { get; }

    /// <summary>The fixture's glow output (lm).</summary>
    public double GlowLumens => RatedLumens * RideParameters.LightGlowFraction;
}
