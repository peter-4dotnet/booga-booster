namespace FourDotnet.BoogaBooster.DigitalTwin.Domain;

/// <summary>Domain helpers for <see cref="LightingMode"/>.</summary>
public static class LightingModeExtensions
{
    /// <summary>
    /// The level a fixture with the given <paramref name="intensity"/> aims for in this
    /// mode (<c>docs/06-ride-lighting.md</c> §6.4): <c>0</c> when off, the glow fraction
    /// <c>γ</c> when glowing, and <c>γ + (1 − γ) · intensity</c> when reactive. Only the
    /// reactive mode reads the intensity.
    /// </summary>
    public static double TargetLevel(this LightingMode mode, double intensity)
    {
        const double glow = RideParameters.LightGlowFraction;

        return mode switch
        {
            LightingMode.Off => 0d,
            LightingMode.Glow => glow,
            LightingMode.Reactive => glow + ((1d - glow) * Math.Clamp(intensity, 0d, 1d)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown lighting mode."),
        };
    }
}
