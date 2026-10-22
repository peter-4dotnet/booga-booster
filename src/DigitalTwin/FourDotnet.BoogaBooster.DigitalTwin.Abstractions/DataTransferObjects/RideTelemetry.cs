namespace FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;

/// <summary>
/// An immutable snapshot of the whole ride at one instant: the mill, the four
/// hubs, the sixteen gondolas, and the overall safety verdict. Emitted at a fixed
/// telemetry rate while the simulation runs.
/// </summary>
/// <param name="State">The ride's lifecycle state.</param>
/// <param name="SimulationTimeSeconds">Simulated time elapsed since the twin started.</param>
/// <param name="IsSafeToStart"><c>true</c> when the ride may be started right now.</param>
/// <param name="SafetyReason">Why the ride is (not) safe to start.</param>
/// <param name="AvailableTransitions">
/// The operator-triggerable states the ride may legally move to from
/// <paramref name="State"/> right now, with each guard already evaluated.
/// Automatic (condition-driven) transitions are deliberately excluded.
/// </param>
/// <param name="BoardedPassengerCount">
/// How many passengers are currently seated on the ride — the number of occupied
/// seats across all sixteen gondolas.
/// </param>
/// <param name="BrakesEngaged">
/// <c>true</c> while the engine brake is engaged (drive power cut and a strong
/// braking torque applied to the mill and hubs), whether commanded by the operator
/// or applied automatically while stopping.
/// </param>
/// <param name="Mill">The central mill's telemetry.</param>
/// <param name="Hubs">The four hubs' telemetry.</param>
/// <param name="Gondolas">The sixteen gondolas' telemetry.</param>
public sealed record RideTelemetry(
    RideState State,
    double SimulationTimeSeconds,
    bool IsSafeToStart,
    RideSafetyReason SafetyReason,
    IReadOnlyList<RideState> AvailableTransitions,
    int BoardedPassengerCount,
    bool BrakesEngaged,
    MillTelemetry Mill,
    IReadOnlyList<HubTelemetry> Hubs,
    IReadOnlyList<GondolaTelemetry> Gondolas);
