namespace FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;

/// <summary>
/// The live telemetry a gondola emits while the ride runs. Rotation is emergent —
/// the gondola has no motor and swings freely under the centrifugal field — so its
/// speed and angle are outputs of the simulation, not commanded inputs.
/// </summary>
/// <param name="HubIndex">The hub (0–3) this gondola hangs from.</param>
/// <param name="Index">The gondola's index (0–3) on its hub.</param>
/// <param name="Brake">Whether the yaw brake is engaged or released.</param>
/// <param name="AngleDegrees">Current yaw angle of the gondola (0–360°).</param>
/// <param name="Rpm">Current yaw rotation speed in revolutions per minute.</param>
/// <param name="LateralG">Sideways specific force felt by riders, in g.</param>
/// <param name="ForwardG">Fore/aft specific force felt by riders, in g.</param>
/// <param name="LoadKg">Total measured passenger load in the gondola.</param>
/// <param name="IsSafeToDispatch">
/// <c>true</c> when every occupied seat's restraint is secured.
/// </param>
/// <param name="Seats">Per-seat sensor readings.</param>
public sealed record GondolaTelemetry(
    int HubIndex,
    int Index,
    GondolaBrakeState Brake,
    double AngleDegrees,
    double Rpm,
    double LateralG,
    double ForwardG,
    double LoadKg,
    bool IsSafeToDispatch,
    IReadOnlyList<SeatTelemetry> Seats);
