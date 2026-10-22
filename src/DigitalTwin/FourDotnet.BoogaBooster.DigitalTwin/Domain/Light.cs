using FourDotnet.BoogaBooster.Core;

namespace FourDotnet.BoogaBooster.DigitalTwin.Domain;

/// <summary>
/// One fixture of the ride's light rig (<c>docs/06-ride-lighting.md</c>), owned by
/// the part it is mounted on. Its state is a single level — the share of its rating
/// it currently emits — that follows a target through an asymmetric first-order
/// envelope: it rises quickly and falls slowly, so it fades rather than cuts, and
/// cannot flash (§6.5, §6.6). Not an aggregate: its changes surface through the
/// owning part.
/// </summary>
public sealed class Light
{
    private double _level;

    public Light(LightRating rating)
    {
        ArgumentNullException.ThrowIfNull(rating);
        Rating = rating;
    }

    /// <summary>The fixture's rated output.</summary>
    public LightRating Rating { get; }

    /// <summary>The share of the rating currently emitted, in [0, 1].</summary>
    public double Level => _level;

    /// <summary>The current output (lm), between 0 and the rating.</summary>
    public double Lumens => _level * Rating.RatedLumens;

    /// <summary>The current outputs (lm) of <paramref name="lights"/>, in order — for telemetry.</summary>
    internal static double[] LumensOf(IReadOnlyList<Light> lights)
    {
        var lumens = new double[lights.Count];
        for (var i = 0; i < lumens.Length; i++)
        {
            lumens[i] = lights[i].Lumens;
        }

        return lumens;
    }

    /// <summary>
    /// Advances the envelope by <paramref name="dt"/> seconds towards
    /// <paramref name="targetLevel"/>. The step is the exact solution for a target
    /// held over <paramref name="dt"/>, so the level moves part of the way towards
    /// the target and never past it.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// The target is not in [0, 1] or the step is not a positive, finite duration.
    /// </exception>
    public void Advance(double targetLevel, double dt)
    {
        if (double.IsNaN(targetLevel) || targetLevel is < 0d or > 1d)
        {
            throw new DomainValidationException("A light's target level must be between 0 and 1.");
        }

        if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0d)
        {
            throw new DomainValidationException("A light's step must be a positive, finite number of seconds.");
        }

        var tau = targetLevel > _level ? RideParameters.LightAttackSeconds : RideParameters.LightDecaySeconds;
        _level += (targetLevel - _level) * (1d - Math.Exp(-dt / tau));
    }
}
