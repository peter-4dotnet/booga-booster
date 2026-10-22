using FourDotnet.BoogaBooster.Core;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;

namespace FourDotnet.BoogaBooster.DigitalTwin.Domain;

/// <summary>
/// A motor-driven hub carrying four gondolas (a driven body of
/// <c>docs/02-rotational-dynamics.md</c>). Every hub receives the same commanded
/// power, but each spins at its own speed because its load-dependent inertia
/// differs — a heavier hub accelerates more slowly and settles slower.
/// </summary>
public sealed class Hub : DomainModel
{
    private readonly Gondola[] _gondolas;
    private EnginePower _power = EnginePower.Off;
    private MotorDirection _direction = MotorDirection.Forward;
    private double _angle;
    private double _omega;

    public Hub(int index)
        : base(isNew: true)
    {
        if (index is < 0 or >= RideParameters.HubCount)
        {
            throw new DomainValidationException($"Hub index must be between 0 and {RideParameters.HubCount - 1}.");
        }

        Index = index;
        _gondolas = new Gondola[RideParameters.GondolasPerHub];
        for (var j = 0; j < _gondolas.Length; j++)
        {
            _gondolas[j] = new Gondola(index, j);
        }
    }

    /// <summary>The hub's index (0–3) on the mill.</summary>
    public int Index { get; }

    /// <summary>The commanded motor power.</summary>
    public EnginePower Power => _power;

    /// <summary>The commanded rotation direction (independent of power).</summary>
    public MotorDirection Direction => _direction;

    /// <summary>Current rotation angle (rad).</summary>
    public double Angle => _angle;

    /// <summary>Current rotation speed (rad/s).</summary>
    public double AngularVelocity => _omega;

    /// <summary>The four gondolas on this hub.</summary>
    public IReadOnlyList<Gondola> Gondolas => _gondolas;

    /// <summary>Total gondola + passenger mass carried by the hub (its "load weight").</summary>
    public double LoadKg
    {
        get
        {
            var sum = 0d;
            foreach (var gondola in _gondolas)
            {
                sum += gondola.TotalMass;
            }

            return sum;
        }
    }

    /// <summary>Combined weight of the seated passengers on this hub (excludes gondola structure).</summary>
    public double PassengerLoadKg
    {
        get
        {
            var sum = 0d;
            foreach (var gondola in _gondolas)
            {
                sum += gondola.PassengerLoadKg;
            }

            return sum;
        }
    }

    /// <summary>Mass of the whole hub assembly (structure + arms + gondolas + riders), used by the mill.</summary>
    public double AssemblyMass =>
        RideParameters.HubStructureKg
        + (RideParameters.GondolasPerHub * RideParameters.HubArmKg)
        + LoadKg;

    /// <summary>Electrical power the hub motor is consuming (watts).</summary>
    public double ConsumedPowerWatts => _power.ToWatts(RideParameters.HubMaxPowerWatts);

    /// <summary>Moment of inertia about the hub axis (kg·m²), recomputed from the current load.</summary>
    public double Inertia
    {
        get
        {
            var arms = RideParameters.GondolasPerHub
                * (RideParameters.HubArmKg * RideParameters.HubArmLength * RideParameters.HubArmLength / 3d);

            var carts = 0d;
            foreach (var gondola in _gondolas)
            {
                carts += gondola.TotalMass * RideParameters.HubArmLength * RideParameters.HubArmLength;
            }

            return arms + carts;
        }
    }

    /// <summary><c>true</c> when the hub has effectively stopped rotating.</summary>
    public bool IsAtRest => Math.Abs(_omega) < 1e-3d;

    /// <summary><c>true</c> when every gondola on the hub is empty.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var gondola in _gondolas)
            {
                if (!gondola.IsEmpty)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Returns the gondola at <paramref name="index"/> on this hub.</summary>
    public Gondola GetGondola(int index)
    {
        if (index < 0 || index >= _gondolas.Length)
        {
            throw new DomainValidationException($"Gondola index must be between 0 and {_gondolas.Length - 1}.");
        }

        return _gondolas[index];
    }

    /// <summary>Sets the commanded motor power (validated by the value object).</summary>
    public bool SetPower(EnginePower power)
    {
        ArgumentNullException.ThrowIfNull(power);
        return ApplyChange(ref _power, power);
    }

    /// <summary>Sets the hub's commanded rotation direction.</summary>
    public bool SetDirection(MotorDirection direction) => ApplyChange(ref _direction, direction);

    /// <summary>Advances natural passenger behaviour across all gondolas.</summary>
    public void AdvanceNaturalBehavior(TimeSpan elapsed)
    {
        foreach (var gondola in _gondolas)
        {
            gondola.AdvanceNaturalBehavior(elapsed);
        }
    }

    /// <summary>
    /// Advances the hub one physics step, then its gondolas. The mill's angle and
    /// speed are threaded through because the gondola field depends on them, and the
    /// engine-brake flag because the hubs brake together with the mill.
    /// </summary>
    public void AdvancePhysics(double millAngle, double millOmega, bool brakesEngaged, double dt)
    {
        var drive = _direction.Sign() * RotationalDynamics.MotorTorque(
            _power.Fraction, _omega, RideParameters.HubStallTorque, RideParameters.HubMaxPowerWatts);

        var coulomb = RideParameters.HubCoulombFriction
            + (brakesEngaged ? RideParameters.HubBrakeTorque : 0d);

        var step = RotationalDynamics.Integrate(
            _omega,
            _angle,
            drive,
            coulomb,
            RideParameters.HubViscousFriction,
            RideParameters.HubAeroDrag,
            Inertia,
            RideParameters.HubMaxAngularVelocity,
            dt);

        _omega = step.Omega;
        _angle = step.Theta;

        foreach (var gondola in _gondolas)
        {
            gondola.AdvancePhysics(millAngle, millOmega, _angle, _omega, dt);
        }
    }

    /// <summary>Engages every gondola's brake (used when the ride comes to rest).</summary>
    public void EngageAllBrakes()
    {
        foreach (var gondola in _gondolas)
        {
            gondola.EngageBrake();
        }
    }

    /// <summary>Releases every gondola's brake (used when the ride starts).</summary>
    public void ReleaseAllBrakes()
    {
        foreach (var gondola in _gondolas)
        {
            gondola.ReleaseBrake();
        }
    }

    /// <summary>Releases every gondola's safety restraints (used when offloading).</summary>
    public void ReleaseAllRestraints()
    {
        foreach (var gondola in _gondolas)
        {
            gondola.ReleaseRestraints();
        }
    }

    /// <summary>Lets passengers leave every gondola once their restraints are released.</summary>
    public void Offload()
    {
        foreach (var gondola in _gondolas)
        {
            gondola.Offload();
        }
    }

    /// <summary>Projects the hub onto the telemetry DTO.</summary>
    public HubTelemetry ToTelemetry() => new(
        Index,
        ConsumedPowerWatts,
        RotationalDynamics.ToRpm(_omega),
        _direction,
        LoadKg);
}
