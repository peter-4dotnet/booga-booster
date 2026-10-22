using FourDotnet.BoogaBooster.Core;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;

namespace FourDotnet.BoogaBooster.DigitalTwin.Domain;

/// <summary>
/// The ride aggregate root (ADR-0003). Every command — set power, board a
/// passenger, work a brake, change lifecycle state — goes through here so the
/// safety invariants live in one place. A guarded state machine owns the ride's
/// lifecycle (<see cref="RideState"/>): it decides which transitions are legal
/// from the current state, evaluates each transition's guard, and runs the entry
/// side-effects (locking/releasing the safety constraints, applying brakes,
/// cutting power). It owns the whole rig through the <see cref="GreatMill"/> and
/// advances the simulation one fixed step at a time.
/// </summary>
public sealed class Ride : DomainModel
{
    /// <summary>One legal operator-triggered edge: a target state and an optional guard.</summary>
    private sealed record Transition(RideState Target, Func<Ride, bool>? Guard);

    /// <summary>
    /// The operator-triggerable transition table — the single description of "what may
    /// follow what". Automatic (condition-driven) transitions are handled in
    /// <see cref="Advance"/> and deliberately excluded here so they are never offered
    /// as a button.
    /// </summary>
    private static readonly IReadOnlyDictionary<RideState, IReadOnlyList<Transition>> OperatorTransitions =
        new Dictionary<RideState, IReadOnlyList<Transition>>
        {
            [RideState.Idle] = [new Transition(RideState.Loading, null)],
            [RideState.Loading] =
            [
                new Transition(RideState.Safe, static ride => ride.IsSafe),
                new Transition(RideState.EmergencyStop, null),
            ],
            [RideState.Safe] =
            [
                new Transition(RideState.Started, static ride => ride.IsSafe),
                new Transition(RideState.Loading, null),
                new Transition(RideState.EmergencyStop, null),
            ],
            [RideState.Started] =
            [
                new Transition(RideState.Stopping, null),
                new Transition(RideState.EmergencyStop, null),
            ],
            [RideState.Stopping] = [new Transition(RideState.EmergencyStop, null)],
            [RideState.Offloading] = [],
            [RideState.EmergencyStop] = [],
        };

    private readonly GreatMill _mill = new();
    private RideState _state = RideState.Idle;
    private double _simulationTimeSeconds;

    private Ride()
        : base(isNew: true)
    {
    }

    /// <summary>Creates a fresh, idle, empty ride.</summary>
    public static Ride Create() => new();

    /// <summary>The ride's lifecycle state (distinct from the base persistence state).</summary>
    public RideState CurrentState => _state;

    /// <summary>Simulated time elapsed since the twin started (seconds).</summary>
    public double SimulationTimeSeconds => _simulationTimeSeconds;

    /// <summary>The central mill (and, through it, the hubs and gondolas).</summary>
    public GreatMill Mill => _mill;

    /// <summary>Why the ride is (not) safe to start right now.</summary>
    public RideSafetyReason SafetyReason => EvaluateSafety();

    /// <summary>
    /// <c>true</c> while the safety constraints are locked — from the moment the ride
    /// starts until it comes to rest and begins offloading. Occupied restraints are
    /// never released while this is <c>true</c>.
    /// </summary>
    public bool ConstraintsLocked =>
        _state is RideState.Started or RideState.Stopping or RideState.EmergencyStop;

    /// <summary>
    /// <c>true</c> while the ride is in motion — running, or braking to a stop. This is
    /// exactly the set of states in which <see cref="Advance"/> steps the physics.
    /// </summary>
    public bool IsRunning =>
        _state is RideState.Started or RideState.Stopping or RideState.EmergencyStop;

    /// <summary>
    /// <c>true</c> while the ride is doing anything an observer would want to watch —
    /// every lifecycle state except <see cref="RideState.Idle"/>. This gates the live
    /// telemetry broadcast, so boarding, accumulating load and securing restraints are
    /// streamed as they happen and not only once the ride is running.
    /// </summary>
    public bool IsActive => _state is not RideState.Idle;

    /// <summary>
    /// The number of completely empty gondolas — the ride's spare boarding capacity.
    /// A group of <c>N</c> needs <c>ceil(N / 2)</c> of these to board.
    /// </summary>
    public int EmptyGondolaCount => _mill.EmptyGondolaCount;

    /// <summary>The free seats a boarding group can take — two per empty gondola.</summary>
    public int FreeSeats => _mill.EmptyGondolaCount * RideParameters.SeatsPerGondola;

    /// <summary><c>true</c> when the ride is at rest and safe to be loaded/started.</summary>
    public bool IsSafeToStart =>
        _state is RideState.Idle or RideState.Loading or RideState.Safe
        && EvaluateSafety() == RideSafetyReason.None;

    /// <summary>
    /// The operator-triggerable states the ride may legally move to right now, with
    /// each guard already evaluated. Automatic transitions are excluded.
    /// </summary>
    public IReadOnlyList<RideState> AvailableTransitions
    {
        get
        {
            if (!OperatorTransitions.TryGetValue(_state, out var edges))
            {
                return [];
            }

            var available = new List<RideState>(edges.Count);
            foreach (var edge in edges)
            {
                if (edge.Guard is null || edge.Guard(this))
                {
                    available.Add(edge.Target);
                }
            }

            return available;
        }
    }

    /// <summary><c>true</c> when every safety interlock is currently satisfied.</summary>
    private bool IsSafe => EvaluateSafety() == RideSafetyReason.None;

    /// <summary>Sets the main (mill) engine power.</summary>
    public bool SetMainEnginePower(EnginePower power)
    {
        ArgumentNullException.ThrowIfNull(power);
        var changed = _mill.SetPower(power);
        MarkChanged(changed);
        return changed;
    }

    /// <summary>
    /// Sets the hub engine power. All four hubs receive the same power; their speeds
    /// still differ because their loads differ.
    /// </summary>
    public bool SetHubEnginePower(EnginePower power)
    {
        ArgumentNullException.ThrowIfNull(power);
        var changed = _mill.SetAllHubPower(power);
        MarkChanged(changed);
        return changed;
    }

    /// <summary>Sets the main (mill) engine rotation direction.</summary>
    public bool SetMainEngineDirection(MotorDirection direction)
    {
        var changed = _mill.SetDirection(direction);
        MarkChanged(changed);
        return changed;
    }

    /// <summary>
    /// Sets the hub engine rotation direction. All four hubs receive the same
    /// direction; their speeds still differ because their loads differ.
    /// </summary>
    public bool SetHubEngineDirection(MotorDirection direction)
    {
        var changed = _mill.SetAllHubDirection(direction);
        MarkChanged(changed);
        return changed;
    }

    /// <summary>Boards a passenger into a specific seat, with a natural restraint-close delay.</summary>
    public void BoardPassenger(
        int hubIndex,
        int gondolaIndex,
        SeatPosition seat,
        Passenger passenger,
        TimeSpan restraintCloseDelay)
    {
        ArgumentNullException.ThrowIfNull(passenger);

        if (_state is not (RideState.Idle or RideState.Loading))
        {
            throw new DomainValidationException($"Passengers can only board while the ride is idle or loading (state: {_state}).");
        }

        _mill.GetHub(hubIndex).GetGondola(gondolaIndex).Board(seat, passenger, restraintCloseDelay);
        _state = RideState.Loading;
        MarkChanged();
    }

    /// <summary>
    /// Boards a whole group as a unit while the ride is idle or loading, seating its
    /// members two per gondola into empty gondolas and letting an odd final member
    /// ride alone — members of a group are never split across a boarding, and a
    /// group never shares a gondola with anyone else. The group boards only when the
    /// ride has enough spare capacity for all of them (<c>ceil(N / 2)</c> empty
    /// gondolas); otherwise nobody is seated. Each seated member's natural
    /// restraint-close delay is drawn from <paramref name="restraintCloseDelay"/>.
    /// Boarding moves the ride into <see cref="RideState.Loading"/>.
    /// </summary>
    /// <param name="selectGondolas">
    /// Chooses which of the empty gondolas the group takes: given the number of empty
    /// gondolas and the number required, it returns that many distinct indices into
    /// the empty-gondola list. In production a random selection is supplied so a
    /// passenger grabs a random gondola; when omitted the empty gondolas are filled in
    /// their natural order (the deterministic default used by tests).
    /// </param>
    /// <exception cref="DomainValidationException">
    /// The group is empty, the ride is not idle or loading, or the ride does not
    /// have <c>ceil(N / 2)</c> empty gondolas to seat every member.
    /// </exception>
    public void BoardGroup(
        IReadOnlyList<PassengerWeight> members,
        Func<TimeSpan> restraintCloseDelay,
        Func<int, int, IReadOnlyList<int>>? selectGondolas = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(restraintCloseDelay);

        if (members.Count == 0)
        {
            throw new DomainValidationException("A boarding group must have at least one member.");
        }

        if (_state is not (RideState.Idle or RideState.Loading))
        {
            throw new DomainValidationException($"Groups can only board while the ride is idle or loading (state: {_state}).");
        }

        var requiredGondolas = (members.Count + 1) / 2;
        if (requiredGondolas > _mill.EmptyGondolaCount)
        {
            throw new DomainValidationException(
                $"The group of {members.Count} needs {requiredGondolas} empty gondola(s) but only {_mill.EmptyGondolaCount} are free.");
        }

        var empty = _mill.EmptyGondolas().ToArray();
        var picks = selectGondolas is null
            ? Enumerable.Range(0, requiredGondolas)
            : selectGondolas(empty.Length, requiredGondolas);

        var member = 0;
        foreach (var index in picks)
        {
            var gondola = empty[index];
            gondola.Board(SeatPosition.Left, new Passenger(members[member++]), restraintCloseDelay());
            if (member < members.Count)
            {
                gondola.Board(SeatPosition.Right, new Passenger(members[member++]), restraintCloseDelay());
            }
        }

        _state = RideState.Loading;
        MarkChanged();
    }

    /// <summary>
    /// Engages or releases the engine brake on the mill and every hub. Engaging cuts
    /// drive power to zero and applies a strong braking torque, bringing a moving
    /// ride to a complete stop within a couple of seconds; releasing lets the ride be
    /// driven again (power stays at zero until commanded). The lifecycle state is left
    /// unchanged, so an operator can brake the ride while it stays
    /// <see cref="RideState.Started"/>.
    /// </summary>
    public void SetEngineBrakes(bool engaged)
    {
        if (engaged)
        {
            _mill.EngageBrakes();
        }
        else
        {
            _mill.ReleaseBrakes();
        }

        MarkChanged();
    }

    /// <summary>Engages or releases a specific gondola's yaw brake.</summary>
    public void SetGondolaBrake(int hubIndex, int gondolaIndex, GondolaBrakeState brake)
    {
        var gondola = _mill.GetHub(hubIndex).GetGondola(gondolaIndex);
        if (brake == GondolaBrakeState.Engaged)
        {
            gondola.EngageBrake();
        }
        else
        {
            gondola.ReleaseBrake();
        }

        MarkChanged();
    }

    /// <summary>
    /// Requests an operator-triggered transition to <paramref name="target"/>. The
    /// state machine is the sole arbiter: a transition that is not defined from the
    /// current state, or whose guard is not satisfied, is rejected and the state is
    /// left unchanged.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// Thrown when the transition is not legal from the current state, or its guard
    /// (for example the ride-safety interlock) is not satisfied.
    /// </exception>
    public void RequestTransition(RideState target)
    {
        if (!OperatorTransitions.TryGetValue(_state, out var edges))
        {
            throw new DomainValidationException($"The ride cannot transition out of {_state}.");
        }

        Transition? edge = null;
        foreach (var candidate in edges)
        {
            if (candidate.Target == target)
            {
                edge = candidate;
                break;
            }
        }

        if (edge is null)
        {
            throw new DomainValidationException($"The ride cannot transition from {_state} to {target}.");
        }

        if (edge.Guard is not null && !edge.Guard(this))
        {
            throw new DomainValidationException(
                $"The ride cannot transition from {_state} to {target}: {DescribeSafety(EvaluateSafety())}");
        }

        ApplyEntry(target);
        _state = target;
        MarkChanged();
    }

    /// <summary>
    /// Advances the whole simulation one fixed step: state-dependent natural passenger
    /// behaviour, the automatic (condition-driven) transitions, and — while in
    /// motion — the physics.
    /// </summary>
    public void Advance(TimeSpan dt)
    {
        if (dt <= TimeSpan.Zero)
        {
            throw new DomainValidationException("The simulation step must be positive.");
        }

        // 1. Natural behaviour depends on the lifecycle state: while loading, seated
        //    passengers secure their restraints; while offloading, they leave.
        switch (_state)
        {
            case RideState.Loading:
            case RideState.Safe:
                _mill.AdvanceNaturalBehavior(dt);
                break;
            case RideState.Offloading:
                _mill.Offload();
                break;
        }

        // 2. Automatic demotion: a ride that is no longer safe drops out of Safe.
        if (_state == RideState.Safe && !IsSafe)
        {
            _state = RideState.Loading;
        }

        // 3. Physics while in motion; a stopping/emergency ride settles into offloading
        //    (releasing the safety constraints) once it reaches a complete rest.
        if (_state is RideState.Started or RideState.Stopping or RideState.EmergencyStop)
        {
            _mill.AdvancePhysics(dt.TotalSeconds);

            if (_state is RideState.Stopping or RideState.EmergencyStop && _mill.IsAtRest)
            {
                _mill.EngageAllGondolaBrakes();
                EnterOffloading();
                _state = RideState.Offloading;
            }
        }

        // 4. Offloading returns to idle once the last rider has left.
        if (_state == RideState.Offloading && _mill.IsEmpty)
        {
            _state = RideState.Idle;
        }

        _simulationTimeSeconds += dt.TotalSeconds;
        MarkChanged();
    }

    /// <summary>Builds an immutable snapshot of the whole ride.</summary>
    public RideTelemetry ToTelemetry()
    {
        var reason = EvaluateSafety();
        var isSafeToStart =
            _state is RideState.Idle or RideState.Loading or RideState.Safe
            && reason == RideSafetyReason.None;

        var hubs = new List<HubTelemetry>(RideParameters.HubCount);
        var gondolas = new List<GondolaTelemetry>(RideParameters.HubCount * RideParameters.GondolasPerHub);
        foreach (var hub in _mill.Hubs)
        {
            hubs.Add(hub.ToTelemetry());
            foreach (var gondola in hub.Gondolas)
            {
                gondolas.Add(gondola.ToTelemetry());
            }
        }

        return new RideTelemetry(
            _state,
            _simulationTimeSeconds,
            isSafeToStart,
            reason,
            AvailableTransitions,
            _mill.BoardedPassengerCount,
            _mill.BrakesEngaged,
            _mill.ToTelemetry(),
            hubs,
            gondolas);
    }

    /// <summary>Runs the entry side-effects for the state being entered.</summary>
    private void ApplyEntry(RideState target)
    {
        switch (target)
        {
            case RideState.Started:
                EnterStarted();
                break;
            case RideState.Stopping:
                EnterStopping();
                break;
            case RideState.EmergencyStop:
                EnterEmergencyStop();
                break;
            case RideState.Offloading:
                EnterOffloading();
                break;
            // Idle, Loading and Safe carry no entry side-effect.
        }
    }

    /// <summary>Locks the safety constraints, releases the engine brake, and releases the gondola brakes so the pods swing.</summary>
    private void EnterStarted()
    {
        _mill.ReleaseBrakes();
        _mill.ReleaseAllGondolaBrakes();
    }

    /// <summary>Cuts power and engages the engine brake for a fast, controlled ramp-down.</summary>
    private void EnterStopping()
    {
        _mill.EngageBrakes();
        _mill.EngageAllGondolaBrakes();
    }

    /// <summary>Immediately cuts power and engages the engine brake from an active state.</summary>
    private void EnterEmergencyStop()
    {
        _mill.EngageBrakes();
        _mill.EngageAllGondolaBrakes();
    }

    /// <summary>The ride is at rest: releases the engine brake and the safety constraints so passengers can leave.</summary>
    private void EnterOffloading()
    {
        _mill.ReleaseBrakes();
        _mill.ReleaseAllRestraints();
    }

    private RideSafetyReason EvaluateSafety()
    {
        foreach (var hub in _mill.Hubs)
        {
            foreach (var gondola in hub.Gondolas)
            {
                if (!gondola.IsSafeToDispatch)
                {
                    return RideSafetyReason.UnsecuredRestraint;
                }
            }
        }

        if (_mill.IsOverloaded)
        {
            return RideSafetyReason.Overloaded;
        }

        if (!_mill.IsBalanced)
        {
            return RideSafetyReason.UnbalancedLoad;
        }

        return RideSafetyReason.None;
    }

    private static string DescribeSafety(RideSafetyReason reason) => reason switch
    {
        RideSafetyReason.UnsecuredRestraint => "an occupied seat's restraint is not secured.",
        RideSafetyReason.UnbalancedLoad => "the load is distributed too unevenly across the arms.",
        RideSafetyReason.Overloaded => $"the combined passenger weight exceeds the {RideParameters.MaxPassengerLoadKg:0} kg maximum load.",
        RideSafetyReason.WrongState => "the ride is not in a startable state.",
        _ => "unknown reason.",
    };
}
