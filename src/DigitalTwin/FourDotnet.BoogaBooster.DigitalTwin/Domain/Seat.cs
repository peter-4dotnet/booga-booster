using FourDotnet.BoogaBooster.Core;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;

namespace FourDotnet.BoogaBooster.DigitalTwin.Domain;

/// <summary>
/// One of a gondola's two seats. It carries the two sensors the safety system
/// depends on: a load cell that measures the occupant's weight, and a restraint
/// lock sensor. It also models the passenger's <em>natural behaviour</em> — after
/// boarding, a rider pulls the restraint down and locks it after a short delay.
/// </summary>
public sealed class Seat : DomainModel
{
    private Passenger? _occupant;
    private RestraintState _restraint = RestraintState.Open;
    private TimeSpan _restraintCloseCountdown = TimeSpan.Zero;

    public Seat(SeatPosition position)
        : base(isNew: true)
    {
        Position = position;
    }

    /// <summary>Which seat this is.</summary>
    public SeatPosition Position { get; }

    /// <summary><c>true</c> when a passenger is boarded.</summary>
    public bool IsOccupied => _occupant is not null;

    /// <summary>Weight measured by the load cell (0 when empty).</summary>
    public double OccupiedKg => _occupant?.Weight.Kilograms ?? 0d;

    /// <summary>The restraint lock sensor reading.</summary>
    public RestraintState Restraint => _restraint;

    /// <summary><c>true</c> when the restraint is closed and locked.</summary>
    public bool IsSecured => _restraint == RestraintState.Secured;

    /// <summary>
    /// <c>true</c> when someone is aboard but their restraint is not yet secured —
    /// the condition that blocks the ride from starting.
    /// </summary>
    public bool NeedsSecuring => IsOccupied && _restraint != RestraintState.Secured;

    /// <summary>
    /// Boards a passenger and schedules their natural restraint-close after
    /// <paramref name="restraintCloseDelay"/> (a value the caller draws from a
    /// sampler so the timing stays deterministic).
    /// </summary>
    public void Board(Passenger passenger, TimeSpan restraintCloseDelay)
    {
        ArgumentNullException.ThrowIfNull(passenger);

        if (IsOccupied)
        {
            throw new DomainValidationException($"The {Position} seat is already occupied.");
        }

        if (restraintCloseDelay < TimeSpan.Zero)
        {
            throw new DomainValidationException("The restraint-close delay cannot be negative.");
        }

        _occupant = passenger;
        _restraint = RestraintState.Open;
        _restraintCloseCountdown = restraintCloseDelay;
        MarkChanged();
    }

    /// <summary>Removes the passenger and resets the restraint. Requires the bar to be open.</summary>
    public void Unboard()
    {
        if (!IsOccupied)
        {
            return;
        }

        if (_restraint != RestraintState.Open)
        {
            throw new DomainValidationException(
                $"Open the {Position} seat's restraint before the passenger can leave.");
        }

        _occupant = null;
        _restraintCloseCountdown = TimeSpan.Zero;
        MarkChanged();
    }

    /// <summary>Pulls the restraint bar down (Open → Closed). Requires an occupant.</summary>
    public void CloseRestraint()
    {
        if (!IsOccupied)
        {
            throw new DomainValidationException($"Cannot close the restraint of the empty {Position} seat.");
        }

        if (_restraint == RestraintState.Secured)
        {
            throw new DomainValidationException("The restraint is already secured; open it first.");
        }

        _restraint = RestraintState.Closed;
        MarkChanged();
    }

    /// <summary>Engages the restraint lock (Closed → Secured).</summary>
    public void SecureRestraint()
    {
        if (_restraint != RestraintState.Closed)
        {
            throw new DomainValidationException("Close the restraint before securing it.");
        }

        _restraint = RestraintState.Secured;
        MarkChanged();
    }

    /// <summary>Releases the restraint back to open, for example to let a passenger leave.</summary>
    public void OpenRestraint()
    {
        _restraint = RestraintState.Open;
        MarkChanged();
    }

    /// <summary>
    /// Advances the passenger's natural behaviour by <paramref name="elapsed"/>:
    /// once the scheduled delay elapses, an occupant who has not yet secured pulls
    /// the bar down and locks it.
    /// </summary>
    public void AdvanceNaturalBehavior(TimeSpan elapsed)
    {
        if (!IsOccupied || _restraint == RestraintState.Secured)
        {
            return;
        }

        if (_restraintCloseCountdown > TimeSpan.Zero)
        {
            _restraintCloseCountdown -= elapsed;
            if (_restraintCloseCountdown > TimeSpan.Zero)
            {
                return;
            }

            _restraintCloseCountdown = TimeSpan.Zero;
        }

        if (_restraint == RestraintState.Open)
        {
            CloseRestraint();
        }

        SecureRestraint();
    }

    /// <summary>Projects the seat's sensors onto the telemetry DTO.</summary>
    public SeatTelemetry ToTelemetry() => new(Position, OccupiedKg, _restraint, IsOccupied, IsSecured);
}
