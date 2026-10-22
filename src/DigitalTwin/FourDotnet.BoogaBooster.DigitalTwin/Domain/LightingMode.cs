namespace FourDotnet.BoogaBooster.DigitalTwin.Domain;

/// <summary>
/// How the light rig behaves in the ride's current lifecycle state
/// (<c>docs/06-ride-lighting.md</c> §6.4). The ride chooses the mode; every fixture
/// turns it and its own intensity into a target level.
/// </summary>
public enum LightingMode
{
    /// <summary>The rig is dark (the ride is idle).</summary>
    Off,

    /// <summary>A steady glow regardless of G (loading, safe, offloading).</summary>
    Glow,

    /// <summary>The glow is a floor and the rest follows intensity (the ride is moving).</summary>
    Reactive,
}
