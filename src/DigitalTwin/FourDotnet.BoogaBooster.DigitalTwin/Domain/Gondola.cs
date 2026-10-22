using FourDotnet.BoogaBooster.Core;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;

namespace FourDotnet.BoogaBooster.DigitalTwin.Domain;

/// <summary>
/// A free-spinning gondola (the passive cart of <c>docs/04-passive-cart-dynamics.md</c>).
/// It has no motor: with the brake released it swings under the centrifugal field
/// as a driven pendulum, its motion set entirely by who is seated where. It owns
/// two <see cref="Seat"/>s, a yaw brake, and its emergent rotation state, and it
/// emits the rider-felt lateral/forward G-forces.
/// </summary>
public sealed class Gondola : DomainModel
{
    private const double HalfPi = Math.PI / 2d;

    private readonly Seat _left = new(SeatPosition.Left);
    private readonly Seat _right = new(SeatPosition.Right);
    private readonly Light _light = new(LightRating.Gondola);

    private GondolaBrakeState _brake = GondolaBrakeState.Engaged;
    private double _angle;       // θ_cart relative to the hub arm (rad)
    private double _omega;       // ω_cart — emergent yaw rate (rad/s)
    private double _lateralG;
    private double _forwardG;

    public Gondola(int hubIndex, int index)
        : base(isNew: true)
    {
        if (hubIndex is < 0 or >= RideParameters.HubCount)
        {
            throw new DomainValidationException($"Hub index must be between 0 and {RideParameters.HubCount - 1}.");
        }

        if (index is < 0 or >= RideParameters.GondolasPerHub)
        {
            throw new DomainValidationException($"Gondola index must be between 0 and {RideParameters.GondolasPerHub - 1}.");
        }

        HubIndex = hubIndex;
        Index = index;

        // Deterministic seed for the nonlinear oscillator so runs stay reproducible
        // (docs §4.7). The small offset keeps the gondola off the knife-edge unstable
        // equilibrium (back pointing inward) so it settles predictably.
        _angle = RotationalDynamics.Wrap(RideKinematics.MountAngle(index) + 0.1d);
    }

    /// <summary>The hub (0–3) this gondola hangs from.</summary>
    public int HubIndex { get; }

    /// <summary>The gondola's index (0–3) on its hub.</summary>
    public int Index { get; }

    /// <summary>The yaw brake state.</summary>
    public GondolaBrakeState Brake => _brake;

    /// <summary>Current yaw angle relative to the hub arm (rad).</summary>
    public double Angle => _angle;

    /// <summary>Current emergent yaw rate (rad/s).</summary>
    public double AngularVelocity => _omega;

    /// <summary>Sideways specific force felt by riders (g). Updated each physics tick.</summary>
    public double LateralG => _lateralG;

    /// <summary>Fore/aft specific force felt by riders (g). Updated each physics tick.</summary>
    public double ForwardG => _forwardG;

    /// <summary>
    /// How hard the ride is working this gondola's riders: the felt horizontal G
    /// (forward and lateral combined) as a share of <see cref="RideParameters.MaxGForce"/>,
    /// clamped to [0, 1] (<c>docs/06-ride-lighting.md</c> §6.2).
    /// </summary>
    public double Intensity =>
        Math.Clamp(double.Hypot(_forwardG, _lateralG) / RideParameters.MaxGForce, 0d, 1d);

    /// <summary>The gondola's own light fixture.</summary>
    public Light Light => _light;

    /// <summary>Total measured passenger weight in the gondola.</summary>
    public double PassengerLoadKg => _left.OccupiedKg + _right.OccupiedKg;

    /// <summary>Mass of the gondola and its riders.</summary>
    public double TotalMass => RideParameters.EmptyGondolaKg + PassengerLoadKg;

    /// <summary>
    /// <c>true</c> when every occupied seat's restraint is secured — the gondola's
    /// contribution to the ride-start interlock.
    /// </summary>
    public bool IsSafeToDispatch => !_left.NeedsSecuring && !_right.NeedsSecuring;

    /// <summary><c>true</c> when neither seat is occupied.</summary>
    public bool IsEmpty => !_left.IsOccupied && !_right.IsOccupied;

    /// <summary>How many of the gondola's two seats are occupied (0–2).</summary>
    public int OccupiedSeatCount =>
        (_left.IsOccupied ? 1 : 0) + (_right.IsOccupied ? 1 : 0);

    /// <summary>Returns the seat at <paramref name="position"/>.</summary>
    public Seat GetSeat(SeatPosition position) =>
        position == SeatPosition.Left ? _left : _right;

    /// <summary>Boards a passenger into the given seat with the given natural restraint-close delay.</summary>
    public void Board(SeatPosition position, Passenger passenger, TimeSpan restraintCloseDelay)
    {
        GetSeat(position).Board(passenger, restraintCloseDelay);
        MarkChanged();
    }

    /// <summary>Engages the brake — the gondola is held and stops rotating.</summary>
    public void EngageBrake()
    {
        _brake = GondolaBrakeState.Engaged;
        _omega = 0d;
        MarkChanged();
    }

    /// <summary>Releases the brake — the gondola is free to swing under the field.</summary>
    public void ReleaseBrake()
    {
        _brake = GondolaBrakeState.Released;
        MarkChanged();
    }

    /// <summary>Releases both safety restraints so seated passengers can leave (offloading).</summary>
    public void ReleaseRestraints()
    {
        _left.OpenRestraint();
        _right.OpenRestraint();
        MarkChanged();
    }

    /// <summary>Lets any seated passengers leave once their restraints are released.</summary>
    public void Offload()
    {
        _left.Unboard();
        _right.Unboard();
        MarkChanged();
    }

    /// <summary>Advances the two seats' natural passenger behaviour by <paramref name="elapsed"/>.</summary>
    public void AdvanceNaturalBehavior(TimeSpan elapsed)
    {
        _left.AdvanceNaturalBehavior(elapsed);
        _right.AdvanceNaturalBehavior(elapsed);
    }

    /// <summary>
    /// Advances the gondola's swing by <paramref name="dt"/> seconds, given the
    /// mill's and its hub's current angles and speeds. Updates the felt G-forces
    /// every call; integrates the pendulum only when the brake is released.
    /// </summary>
    public void AdvancePhysics(double millAngle, double millOmega, double hubAngle, double hubOmega, double dt)
    {
        var pivot = RideKinematics.PivotPosition(millAngle, HubIndex, hubAngle, Index);
        var hubCentre = RideKinematics.HubCentre(millAngle, HubIndex);
        var field = RideKinematics.OutwardField(pivot, hubCentre, millOmega, hubOmega);

        var (comDistance, comAngle) = CentreOfMass();
        var worldFacing = millAngle + hubAngle + RideKinematics.MountAngle(Index) + _angle;

        UpdateGForces(field, worldFacing, comAngle, comDistance);

        if (_brake == GondolaBrakeState.Engaged)
        {
            _omega = 0d;
            return;
        }

        var mass = TotalMass;
        var inertia = RideParameters.EmptyGondolaPivotInertia + (mass * comDistance * comDistance);

        // Driven-pendulum torque: the field pulls the centre of mass (the back) to
        // point outward; pivot damping bleeds the swing. No motor term.
        var fieldMagnitude = field.Length;
        var fieldAngle = field.Angle;
        var comWorldAngle = worldFacing + comAngle;
        var drive = mass * comDistance * fieldMagnitude * Math.Sin(fieldAngle - comWorldAngle);
        var torque = drive - (RideParameters.GondolaPivotDamping * _omega);

        var alpha = torque / inertia;
        _omega += alpha * dt;                                   // semi-implicit Euler
        _angle = RotationalDynamics.Wrap(_angle + (_omega * dt));
    }

    /// <summary>Advances the gondola's light by <paramref name="dt"/> seconds towards its target in <paramref name="mode"/>.</summary>
    public void AdvanceLights(LightingMode mode, double dt) =>
        _light.Advance(mode.TargetLevel(Intensity), dt);

    /// <summary>Projects the gondola's state and sensors onto the telemetry DTO.</summary>
    public GondolaTelemetry ToTelemetry() => new(
        HubIndex,
        Index,
        _brake,
        _angle * 180d / Math.PI,
        RotationalDynamics.ToRpm(_omega),
        _lateralG,
        _forwardG,
        PassengerLoadKg,
        IsSafeToDispatch,
        [_left.ToTelemetry(), _right.ToTelemetry()],
        _light.Lumens);

    /// <summary>
    /// The world direction the gondola's back (its centre of mass) currently points.
    /// At the settled equilibrium of a constant-speed symmetric load this equals the
    /// outward radial — the "unbluffable" test in <c>docs/04</c>.
    /// </summary>
    internal double BackWorldAngle(double millAngle, double hubAngle)
    {
        var (_, comAngle) = CentreOfMass();
        return RotationalDynamics.Wrap(
            millAngle + hubAngle + RideKinematics.MountAngle(Index) + _angle + comAngle);
    }

    private void UpdateGForces(PlanarVector field, double worldFacing, double comAngle, double comDistance)
    {
        // Horizontal specific force ≈ the centrifugal field at the pivot plus the
        // gondola's own swing term at the seats (which sit near the centre of mass).
        var swing = PlanarVector.FromAngle(worldFacing + comAngle) * (_omega * _omega * comDistance);
        var specific = field + swing;

        var forwardHat = PlanarVector.FromAngle(worldFacing);
        var lateralHat = PlanarVector.FromAngle(worldFacing + HalfPi);

        SetGForces(
            specific.Dot(forwardHat) / RideParameters.Gravity,
            specific.Dot(lateralHat) / RideParameters.Gravity);
    }

    /// <summary>Records the forward and lateral G the riders feel (validated: both finite).</summary>
    /// <exception cref="DomainValidationException">Either value is not a finite number.</exception>
    internal void SetGForces(double forwardG, double lateralG)
    {
        if (!double.IsFinite(forwardG) || !double.IsFinite(lateralG))
        {
            throw new DomainValidationException("Felt G-forces must be finite numbers.");
        }

        _forwardG = forwardG;
        _lateralG = lateralG;
    }

    /// <summary>
    /// The gondola's centre of mass about the pivot, as a distance (<c>r_g</c>) and a
    /// direction in the gondola's own frame. Loading changes both: two riders push it
    /// further back (bigger <c>r_g</c>, harder swing); one-sided loading shifts it
    /// sideways (a lopsided equilibrium). The pivot sits forward, so the empty
    /// gondola's mass already sits behind it.
    /// </summary>
    private (double Distance, double Angle) CentreOfMass()
    {
        var mass = TotalMass;
        var leftKg = _left.OccupiedKg;
        var rightKg = _right.OccupiedKg;

        // Pivot at origin; +x forward, −x back, +y left, −y right.
        var x = RideParameters.EmptyGondolaKg * -RideParameters.EmptyPivotToComMeters;
        x += (leftKg + rightKg) * -RideParameters.PassengerBackOffsetMeters;

        var y = leftKg * RideParameters.PassengerLateralOffsetMeters;
        y -= rightKg * RideParameters.PassengerLateralOffsetMeters;

        x /= mass;
        y /= mass;

        var vector = new PlanarVector(x, y);
        return (vector.Length, vector.Angle);
    }
}
