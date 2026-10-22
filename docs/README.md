# BoogaBooster — Physics & Simulation Docs

These documents describe **how the digital twin operates technically**: the physical
model, the maths behind every telemetry channel, and the numerical scheme the engine
runs each tick. They are the derivations *behind* the tests — the physics is the
ground truth the code is written against (see the BoogaBooster project plan, §10).

> **Scope.** These docs cover the *domain / physics engine* only. They deliberately
> know nothing about ASP.NET, SignalR, or the frontend. The physics is a pure,
> deterministic, fixed-timestep function of state.

## The ride, in one paragraph

BoogaBooster is a three-level nested rotating rig spinning about a single vertical
axis. A **Great Mill** (1 motor-driven spindle, four 6 m arms) carries four **Hubs**
(each motor-driven, four 2 m arms). Each hub arm tip carries a **Cart** — 16 in total —
and the carts have **no motor**: they free-spin on an offset vertical pivot and swing
passively under the centrifugal field. That's **5 driven axes + 16 free axes = 21
rotating bodies** and **32 seats**.

## Reading order

| # | Document | What it covers |
|---|----------|----------------|
| 1 | [01-physics-overview.md](01-physics-overview.md) | Coordinate frames, the fixed-timestep tick, determinism, the state vector |
| 2 | [02-rotational-dynamics.md](02-rotational-dynamics.md) | `τ = Iα`, the symplectic integrator, load-dependent moment of inertia |
| 3 | [03-motor-power-and-torque.md](03-motor-power-and-torque.md) | Engine power → torque curve, friction, aerodynamic drag, terminal speed |
| 4 | [04-passive-cart-dynamics.md](04-passive-cart-dynamics.md) | The free-spinning carts as driven pendulums; why seating changes everything |
| 5 | [05-forces-and-g-forces.md](05-forces-and-g-forces.md) | Nested-frame kinematics, centripetal superposition, the G-force beat, imbalance |
| 6 | [06-ride-lighting.md](06-ride-lighting.md) | The G-reactive light rig: fixtures and ratings, intensity, propagation, lifecycle floors, the flash-safe envelope |

## Notation & units

SI throughout. Angles in **radians**, angular rate in **rad/s**, angular acceleration
in **rad/s²**, torque in **N·m**, inertia in **kg·m²**, force in **N**, `g = 9.81 m/s²`.

| Symbol | Meaning | Unit |
|--------|---------|------|
| `θ` | angle | rad |
| `ω = θ̇` | angular velocity | rad/s |
| `α = ω̇` | angular acceleration | rad/s² |
| `I` | moment of inertia | kg·m² |
| `τ` | torque | N·m |
| `dt` | fixed timestep (`1/120 s`) | s |

Default numeric parameters live in **[appendix-parameters.md](appendix-parameters.md)**.
