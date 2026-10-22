using FourDotnet.BoogaBooster.DigitalTwin.Domain;
using Xunit;

namespace FourDotnet.BoogaBooster.DigitalTwin.Tests;

/// <summary>
/// The flash-safety proof of <c>docs/06-ride-lighting.md</c> §6.6: the worst-case
/// input — a full-swing square wave on the target between the glow and full output —
/// swept across frequencies at the fixed tick, never makes a fixture flash more than
/// three times in any one-second window.
/// </summary>
public sealed class LightFlashSafetyTests
{
    private const int MaxFlashesPerSecond = 3;
    private const double FlashThreshold = 0.2d;   // 20 % of the rating
    private const double SweepSeconds = 12d;

    private static readonly double Dt = TestHelpers.Dt.TotalSeconds;

    [Fact]
    public void A_square_wave_swept_from_a_quarter_to_thirty_hertz_never_flashes_more_than_three_times_a_second()
    {
        var worst = 0;
        var worstFrequency = 0d;

        for (var step = 0; step <= 595; step++)
        {
            var frequency = 0.25d + (step * 0.05d);
            var flashes = MaxFlashesInAnySecond(frequency);
            if (flashes > worst)
            {
                worst = flashes;
                worstFrequency = frequency;
            }
        }

        Assert.True(
            worst <= MaxFlashesPerSecond,
            $"{worst} flashes in one second at {worstFrequency:0.00} Hz; the limit is {MaxFlashesPerSecond}.");
    }

    [Fact]
    public void G_alternating_at_five_hertz_does_not_flash()
    {
        // The spec scenario: G alternating between 0 and the maximum at 5 Hz while
        // Started drives the target between the glow and full output.
        Assert.True(MaxFlashesInAnySecond(5d) <= MaxFlashesPerSecond);
    }

    /// <summary>
    /// Drives one fixture with the square wave and counts flashes per §6.6: a
    /// transition fires when the level moves the threshold away from the last
    /// extreme; a flash is a pair of opposing transitions.
    /// </summary>
    private static int MaxFlashesInAnySecond(double frequency)
    {
        var light = new Light(LightRating.Gondola);
        var glow = RideParameters.LightGlowFraction;
        var steps = (int)Math.Round(SweepSeconds / Dt);

        // Start settled at the glow so the first rise is a real transition.
        for (var i = 0; i < 600; i++)
        {
            light.Advance(glow, Dt);
        }

        var transitions = new List<double>();
        var direction = 0;
        var peak = light.Level;
        var trough = light.Level;

        for (var k = 0; k < steps; k++)
        {
            var t = k * Dt;
            var high = ((long)Math.Floor(2d * frequency * t)) % 2 == 0;
            light.Advance(high ? 1d : glow, Dt);
            var level = light.Level;

            if (direction >= 0)
            {
                peak = Math.Max(peak, level);
                if (level <= peak - FlashThreshold)
                {
                    transitions.Add(t);
                    direction = -1;
                    trough = level;
                    continue;
                }
            }

            if (direction <= 0)
            {
                trough = Math.Min(trough, level);
                if (level >= trough + FlashThreshold)
                {
                    transitions.Add(t);
                    direction = 1;
                    peak = level;
                }
            }
        }

        var worst = 0;
        for (var i = 0; i < transitions.Count; i++)
        {
            var j = i;
            while (j < transitions.Count && transitions[j] < transitions[i] + 1d)
            {
                j++;
            }

            worst = Math.Max(worst, (j - i) / 2);
        }

        return worst;
    }
}
