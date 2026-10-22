using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;

namespace FourDotnet.BoogaBooster.DigitalTwin.Abstractions;

/// <summary>
/// Cross-module read access to the ride's current telemetry snapshot. Other
/// modules depend on this abstraction only — never on the DigitalTwin module's
/// internals (ADR-0004).
/// </summary>
public interface IRideTelemetryProvider
{
    /// <summary>Returns a consistent snapshot of the ride's current telemetry.</summary>
    RideTelemetry GetTelemetry();
}
