using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;
using FourDotnet.BoogaBooster.DigitalTwin.Domain;

namespace FourDotnet.BoogaBooster.DigitalTwin.Application;

/// <summary>
/// Holds the single in-memory <see cref="Ride"/> aggregate behind a thread-safe
/// boundary shared by the simulation loop (writer) and the command/query handlers.
/// Every operation returns a consistent telemetry snapshot of the resulting state.
/// The ride is ephemeral: it starts idle and empty and is not persisted.
/// </summary>
public interface IRideStore
{
    /// <summary>Returns a consistent snapshot of the ride's current telemetry.</summary>
    RideTelemetry GetTelemetry();

    /// <summary>
    /// <c>true</c> while the ride is active — in any lifecycle state except
    /// <see cref="RideState.Idle"/>. The condition under which the live telemetry
    /// broadcast emits frames, so boarding and loading are observable and not only
    /// the running ride.
    /// </summary>
    bool IsActive { get; }

    /// <summary>The ride's current lifecycle state (a cheap read for the loading coordinator).</summary>
    RideState CurrentState { get; }

    /// <summary>
    /// The number of completely empty gondolas — the ride's spare boarding capacity.
    /// A group of <c>N</c> needs <c>ceil(N / 2)</c> of these to board.
    /// </summary>
    int EmptyGondolaCount { get; }

    /// <summary>Advances the simulation by one fixed <paramref name="dt"/> step.</summary>
    RideTelemetry Advance(TimeSpan dt);

    /// <summary>Sets the main (mill) engine power.</summary>
    RideTelemetry SetMainEnginePower(EnginePower power);

    /// <summary>Sets the hub engine power (applied identically to all four hubs).</summary>
    RideTelemetry SetHubEnginePower(EnginePower power);

    /// <summary>Sets the main (mill) engine rotation direction.</summary>
    RideTelemetry SetMainEngineDirection(MotorDirection direction);

    /// <summary>Sets the hub engine rotation direction (applied identically to all four hubs).</summary>
    RideTelemetry SetHubEngineDirection(MotorDirection direction);

    /// <summary>
    /// Boards a passenger into a seat. When <paramref name="weight"/> is
    /// <c>null</c> a random weight is drawn; the natural restraint-close delay is
    /// always drawn from the sampler.
    /// </summary>
    RideTelemetry BoardPassenger(int hubIndex, int gondolaIndex, SeatPosition seat, PassengerWeight? weight);

    /// <summary>
    /// Boards a whole group as a unit (idle/loading only), seating its members two
    /// per gondola with an odd member alone. The group boards only when the ride has
    /// <c>ceil(N / 2)</c> empty gondolas; otherwise the domain rejects it and nobody
    /// is seated. Each member's natural restraint-close delay is drawn from the sampler.
    /// </summary>
    RideTelemetry BoardGroup(IReadOnlyList<PassengerWeight> members);

    /// <summary>Engages or releases a specific gondola's yaw brake.</summary>
    RideTelemetry SetGondolaBrake(int hubIndex, int gondolaIndex, GondolaBrakeState brake);

    /// <summary>
    /// Engages or releases the engine brake on the mill and every hub. Engaging cuts
    /// drive power and applies a strong braking torque for a fast stop.
    /// </summary>
    RideTelemetry SetEngineBrakes(bool engaged);

    /// <summary>
    /// Requests an operator-triggered lifecycle transition to <paramref name="target"/>.
    /// The domain state machine rejects an illegal or guard-failing transition.
    /// </summary>
    RideTelemetry RequestStateTransition(RideState target);

    /// <summary>Starts the ride (transition to <see cref="RideState.Started"/>), if safe.</summary>
    RideTelemetry StartRide();

    /// <summary>Begins a controlled ramp down (transition to <see cref="RideState.Stopping"/>).</summary>
    RideTelemetry StopRide();
}
