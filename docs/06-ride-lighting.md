# 6. Ride Lighting — Making the G-Force Visible

The physics (docs 1–5) computes what every rider feels. This document turns that into
something you can *see*: a light rig on every moving part of the ride whose brightness
follows the G-force. It defines the fixtures, the intensity signal, how intensity
propagates up the rig, how the lifecycle state sets a floor, and the smoothing envelope
that keeps the rig from ever flashing.

Like the physics, the rig is a pure, deterministic function of the ride's state and the
fixed `dt` steps taken. Its output is a physical quantity — **lumens** — not a UI effect.

---

## 6.1 Fixtures and ratings

Every moving part carries one fixture, mounted on the part it lights:

| Fixture | Count | Rated output `Φ_max` | Glow output `0.15 · Φ_max` |
|---------|------:|---------------------:|---------------------------:|
| Mill crown | 1 | 5 000 lm | 750 lm |
| Mill arm | 4 | 4 000 lm | 600 lm |
| Hub | 4 | 2 500 lm | 375 lm |
| Hub arm | 16 | 2 500 lm | 375 lm |
| Gondola | 16 | 1 000 lm | 150 lm |
| **Rig** | **41** | **87 000 lm** | **13 050 lm** |

The rig total is `5 000 + 4·4 000 + 4·2 500 + 16·2 500 + 16·1 000 = 87 000 lm`.

Mill arms are indexed by the hub they carry; hub arms by the gondola they carry. A
fixture's state is a single **level** `ℓ ∈ [0, 1]`, the share of its rating it emits:

```
Φ = ℓ · Φ_max
```

---

## 6.2 Gondola intensity

A gondola's **intensity** is the horizontal G its riders feel (doc 5 §5.1), normalised
against the ride's safety limit and clamped:

```
i_gondola = clamp( √(G_fwd² + G_lat²) / G_max , 0, 1 )        G_max = 4.5 g
```

Forward and lateral G are orthogonal components of the same horizontal specific force,
so their vector magnitude is the felt horizontal load: `G_fwd = 0.6 g`, `G_lat = 0.8 g`
give `1.0 g` and `i = 1.0 / 4.5 ≈ 0.222`. At half the limit (`2.25 g`) the intensity is
exactly `0.5`; anything at or beyond `G_max` reads `1`.

The vertical 1 g of gravity is deliberately left out: it is always there, so it carries
no information about how hard the ride is working.

---

## 6.3 Propagation up the rig — by maximum

Fixtures above the gondolas take the **highest** intensity among the gondolas they
carry:

```
i_hubArm[j]  = i_gondola[j]
i_hub        = max_j  i_gondola[j]               (the hub's 4 gondolas)
i_millArm[h] = i_hub[h]
i_crown      = max_h  i_hub[h]                   (= max over all 16 gondolas)
```

The maximum (not the mean) is the right operator for a light rig: one hard-hit gondola
lights its whole branch up to the crown, so the rig always shows *where* the ride is
working hardest. A mean would wash a single peak out across four gondolas.

Intensities are derived from the gondolas' G on demand; no intermediate value is stored.

---

## 6.4 Lifecycle floors — the target level

The ride's lifecycle state chooses one of three **lighting modes**, and the mode turns
a fixture's intensity into its **target level** `ℓ*`:

```
              ┌ 0                          Off       — Idle
ℓ*(i)    =    ┤ γ                          Glow      — Loading, Safe, Offloading
              └ γ + (1 − γ) · i            Reactive  — Started, Stopping, EmergencyStop

γ = 0.15  (glow fraction)
```

- **Idle** — the ride is closed; the rig goes dark.
- **Loading, Safe, Offloading** — riders are boarding or leaving; a steady 15 % glow
  makes the ride inviting. G is ignored: the ride only integrates physics while
  `Started`, `Stopping` or `EmergencyStop`, so here the G values are stale, not zero.
- **Started, Stopping, EmergencyStop** — the ride is moving; the glow becomes a floor
  and the remaining 85 % of the rating follows intensity. An emergency stop stays
  reactive, exactly like a normal run.

Settled outputs, for reference:

| Situation | Gondola | Mill crown |
|-----------|--------:|-----------:|
| Idle | 0 lm | 0 lm |
| Loading / Safe / Offloading | 150 lm | 750 lm |
| Started, `i = 0.5` | 150 + 0.5·850 = **575 lm** | 750 + 0.5·4 250 = 2 875 lm |
| Started, `i = 1` | 1 000 lm | 5 000 lm |

---

## 6.5 The asymmetric envelope

A fixture never jumps to its target. Each tick it moves towards it with a first-order
envelope whose time constant depends on direction:

```
τ   = ℓ* > ℓ ? τ_attack : τ_decay          τ_attack = 0.1 s,  τ_decay = 0.8 s
ℓ  ← ℓ + (ℓ* − ℓ) · (1 − e^(−dt/τ))
```

Properties of this step:

- **Exact.** For a target held constant over `dt` it is the closed-form solution of
  `ℓ̇ = (ℓ* − ℓ)/τ`, not an Euler approximation, so it is correct at any `dt`.
- **Unconditionally stable, no overshoot.** `0 < 1 − e^(−dt/τ) < 1`, so `ℓ` moves a
  fraction of the way towards `ℓ*` and never past it. With `ℓ*` and the start value in
  `[0, 1]`, `ℓ` stays in `[0, 1]` without clamping.
- **Deterministic.** Only `Math.Exp` on `double`; reproducible for a given runtime and
  platform, like the rest of the physics.

At `dt = 1/120 s` a tick covers `1 − e^(−1/12) ≈ 8.0 %` of the gap when rising and
`1 − e^(−1/96) ≈ 1.04 %` when falling. A light reaches 95 % of a step (`3τ`) in
**0.3 s** rising and **2.4 s** falling. After `Offloading → Idle` the glow fades to
1 % of its value in `τ_decay · ln 100 ≈ 3.7 s`: the rig fades out, it doesn't cut off.

The asymmetry is the point. A fast attack makes G peaks feel immediate; a slow decay
keeps the rig from chasing every swing of the free-spinning gondolas (doc 4).

---

## 6.6 Flash safety

**Requirement:** no fixture completes more than **3 flashes in any one-second window**,
for any G input (the WCAG 2.3.1 general-flash threshold).

**Definition.** A *transition* is a change in output of at least 20 % of the rating
away from the last extreme (a hysteresis of `0.2` on `ℓ`). A *flash* is a pair of
opposing transitions — a fall followed by a rise, or the reverse. The flashes in a
window are half the transitions in it, rounded down.

**Worst-case input.** Intensity can only push the target between `γ` and `1`, so the
largest possible excitation is a full-swing square wave on the target between
`ℓ* = 0.15` and `ℓ* = 1.0`. Sweeping its frequency from 0.25 Hz to 30 Hz in 0.05 Hz
steps at `dt = 1/120 s`:

| `τ_decay` | Worst case | At |
|----------:|-----------:|---:|
| **0.8 s** (chosen) | **2 flashes/s** | ≈ 1.35 Hz |
| 0.5 s | 3 flashes/s | ≈ 2.40 Hz |
| 0.3 s | 5 flashes/s | ≈ 4.45 Hz |

Below the worst-case frequency the input is too slow to flash often; above it, the slow
decay can't fall 20 % within a half-period, so the light simply stays bright. With
`τ_decay = 0.8 s` the rig has a margin of one flash per second below the limit, by
construction of the envelope rather than by limiting the input.

`τ_attack` barely matters here: it only has to reach the next extreme, which it does in
a fraction of any half-period that the decay allows.

---

## 6.7 Where it runs in the tick

The rig advances as the **last** step of every tick, in every lifecycle state —
including `Idle`, so the rig fades out after offloading:

```
5. lights:  mode = mode(state)                       // after any state transition this tick
            crown       ← ℓ*(i_crown)
            millArm[h]  ← ℓ*(i_hub[h])
            hub[h]      ← ℓ*(i_hub[h]);  hubArm[h][j] ← ℓ*(i_gondola[h][j])
            gondola     ← ℓ*(i_gondola)
```

Running after physics means the G values it reads are the ones just computed this tick.

---

## 6.8 What this doc gives the test suite

- **Fixture count and ratings:** 1 + 4 + 4 + 16 + 16 = 41 fixtures, 87 000 lm rated.
- **Intensity:** `(0.6, 0.8) g → 1.0 g`; `2.25 g → 0.5`; `≥ 4.5 g → 1`.
- **Propagation:** one gondola at 0.9 among three at 0.2 lights its hub and mill arm at
  0.9; the other hub arms stay at 0.2; the crown follows the global maximum.
- **Settled floors:** 0 lm idle; 150 lm (gondola) and 750 lm (crown) while loading;
  575 lm for a gondola at `i = 0.5` while started.
- **Envelope:** rises faster than it falls; never overshoots; stays in `[0, 1]`.
- **Flash safety:** the §6.6 sweep yields ≤ 3 flashes/s, and fails with `τ_decay = 0.3 s`.
- **Determinism:** two identical rides advanced through the same steps report identical
  lumens on every fixture after every step.
