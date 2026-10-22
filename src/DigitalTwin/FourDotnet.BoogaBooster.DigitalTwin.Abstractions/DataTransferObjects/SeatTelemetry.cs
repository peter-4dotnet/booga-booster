namespace FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;

/// <summary>
/// The live reading of a single seat's sensors: which seat it is, how much weight
/// its load cell measures, and what its restraint lock sensor reports.
/// </summary>
/// <param name="Position">Which seat this is.</param>
/// <param name="OccupiedKg">Weight measured by the seat's load cell (0 when empty).</param>
/// <param name="Restraint">The restraint lock sensor reading.</param>
/// <param name="IsOccupied">
/// <c>true</c> when a passenger is boarded in this seat. Distinguishes a genuinely
/// empty seat from an occupied one whose measured weight happens to be zero, so
/// consumers never have to infer occupancy from <paramref name="OccupiedKg"/>.
/// </param>
/// <param name="IsSecured">
/// <c>true</c> when the seat is occupied and its restraint has locked
/// (<see cref="RestraintState.Secured"/>). <c>false</c> while an occupant is still
/// securing their restraint during the loading countdown.
/// </param>
public sealed record SeatTelemetry(
    SeatPosition Position,
    double OccupiedKg,
    RestraintState Restraint,
    bool IsOccupied,
    bool IsSecured);
