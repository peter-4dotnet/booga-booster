# Appendix — Default Parameters

The starting configuration for the physics engine. These are tuned to give coherent,
playable numbers — a believable big flat-ride — not to model a specific real machine.
Tune to taste; every one of them should be a named constant / config value, never a magic
number buried in the physics.

## Masses & geometry

| Parameter | Symbol | Default | Unit |
|-----------|--------|--------:|------|
| Passenger mass | `m_pax` | 75 | kg |
| Empty cart mass | `m_cart` | 150 | kg |
| Hub structure mass | `m_hub` | 400 | kg |
| Mill arm mass | `m_arm` | 800 | kg |
| Mill arm length | `L_mill` | 6 | m |
| Hub arm length | `L_hub` | 2 | m |
| Seat offset | `r_seat` | 0.5 | m |
| Pivot→CoM offset (empty cart) | `r_g0` | 0.35 | m |
| Cart yaw inertia (empty, about pivot) | `I_cart_cm` | 220 | kg·m² |

## Dynamics & drive

| Parameter | Symbol | Default | Unit |
|-----------|--------|--------:|------|
| Timestep | `dt` | 1/120 | s |
| Telemetry rate | — | 30 | Hz |
| Mill stall torque | `τ_stall` | 60,000 | N·m |
| Mill max power | `P_max` | 90 | kW |
| Cart pivot damping | `c` | 900 | N·m·s/rad |
| Gravity | `g` | 9.81 | m/s² |

## Safety limits

| Parameter | Symbol | Default | Unit |
|-----------|--------|--------:|------|
| Max allowed G | `G_max` | 4.5 | g |
| Over-speed cap (mill) | `ω_max` | 2.5 | rad/s |
| Imbalance dispatch limit | — | 50 | mm |
| Wind cutoff | `windMax` | 18 | m/s |
| E-stop decel ramp | — | 8 | s |

## Loss-model coefficients

Not fixed by the plan — picked to give the sanity-check behaviour below and locked in as the
golden-scenario baseline. The coefficients are sized against each body's rotating inertia so
a de-powered rig coasts to rest in a believable time (doc 3 §3.4); the increase is biased
toward Coulomb/viscous friction (which govern the low-speed tail) over aerodynamic drag
(which mostly sets terminal speed).

| Parameter | Symbol | Role | Mill | Hub |
|-----------|--------|------|-----:|----:|
| Coulomb friction | `C_coulomb` | constant bearing drag (sets a clean, bounded-time stop) | 10,000 | 400 |
| Viscous friction | `C_viscous` | speed-proportional bearing drag | 3,500 | 500 |
| Aerodynamic drag | `k_aero` | ω² air resistance (sets terminal speed) | 6,000 | 200 |
| Motor speed floor | `ω_eps` | ~1e-3 rad/s, divide-by-zero guard (doc 3 §3.1) | 1e-3 | 1e-3 |

Units: `C_coulomb` in N·m, `C_viscous` in N·m·s/rad, `k_aero` in N·m·s²/rad².

## Lighting

The G-reactive light rig (doc 6). Ratings are per fixture.

| Parameter | Symbol | Default | Unit |
|-----------|--------|--------:|------|
| Mill crown rating (×1) | `Φ_crown` | 5,000 | lm |
| Mill arm rating (×4) | `Φ_millArm` | 4,000 | lm |
| Hub rating (×4) | `Φ_hub` | 2,500 | lm |
| Hub arm rating (×16) | `Φ_hubArm` | 2,500 | lm |
| Gondola rating (×16) | `Φ_gondola` | 1,000 | lm |
| Glow fraction | `γ` | 0.15 | — |
| Attack time constant | `τ_attack` | 0.1 | s |
| Decay time constant | `τ_decay` | 0.8 | s |
| Rig total rating (41 fixtures) | `Φ_rig` | 87,000 | lm |

**Sanity check.** `5,000 + 4·4,000 + 4·2,500 + 16·2,500 + 16·1,000 = 87,000 lm`. The
idle rig emits 0 lm; a loading rig settles at its glow, `0.15 · 87,000 = 13,050 lm`
(150 lm per gondola, 750 lm at the crown); a gondola at half the allowed G while running
settles at `150 + 0.5 · 850 = 575 lm`. A step rises to 95 % in `3τ_attack = 0.3 s` and
falls to 95 % in `3τ_decay = 2.4 s`. The worst-case square-wave input produces at most
**2 flashes per second** (doc 6 §6.6), below the limit of 3.

## Sanity check

With these parameters, a **full ride** (~2.4 t of carts + people orbiting at 6 m):

- has a mill moment of inertia around **2×10⁵ kg·m²**,
- reaches cruise in **~15–25 s** on 90 kW,
- settles at a natural terminal speed of **~2.1 rad/s** — a little below the `ω_max = 2.5`
  safety cap, because the losses (dominated by aero drag at cruise) balance the motor
  before the cap; the cap is a ceiling, not the operating point,
- produces roughly **~2 g** at the seats at that terminal speed,
- and, once power is cut, **coasts to rest in ~30 s empty / ~50 s fully loaded** (the loaded
  rig carries ~2.5× the inertia, so it coasts longer).

If your numbers land far outside those ranges, something in the inertia model (doc 2) or
the motor/loss balance (doc 3) is off.
