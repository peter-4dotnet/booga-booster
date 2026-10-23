# Design

## Context

- `Gondola.UpdateGForces` sets `ForwardG`/`LateralG` inside `AdvancePhysics`, and
  `Ride.Advance` only calls physics in `Started`, `Stopping` and `EmergencyStop`. In
  every other state the G values are stale, not zero.
- `Gondola`, `Hub` and `GreatMill` derive from `DomainModel` and expose step-style
  mutators (`AdvancePhysics`, `AdvanceNaturalBehavior`). `Ride.Advance` calls
  `MarkChanged()` once per tick.
- Telemetry records are built by each part's `ToTelemetry()`. After
  `telemetry-dto-layout` they live in `DigitalTwin.Abstractions/DataTransferObjects/`.
- The 3D scene (`ride-visualization.ts`) already has a node for each of the 41 attach
  points: `mainPivot`, one `armHinge`/`arm` and `hubPivot` per hub, and one
  `hubArmHinge`/`hubArm` and `gondolaPivot` per gondola.
- The dashboard's left rail is the `Controls` landmark (status, queue, operation
  controls, weather). The right rail is `Telemetry` (security, speed, gondolas).

## Goals / Non-Goals

**Goals:**
- Lights are domain hardware, advanced inside the deterministic tick, with every
  constant in `RideParameters` and derived in `docs/06-ride-lighting.md`.
- Flash safety holds by construction (the envelope constants) and is proven by a test.

**Non-Goals:**
- Colour, strobe or chase patterns. The rig is monochrome lumens.
- Lighting programmes or operator overrides (future `Controller` work).
- An emergency-stop override to full brightness (possible `passenger-safety`
  follow-up).
- A light-output metric or gauge on the Aspire dashboard (ADR-0009 doesn't require one:
  there's no new handler).

## Decisions

### D1. A `Light` domain type owned by each part

`Domain/Light.cs` is a sealed class owned by its part, not an aggregate root and not a
`DomainModel`. Its changes surface through the owning aggregate, whose `MarkChanged()`
`Ride.Advance` already calls. It has:

- `LightRating` (value object, adr-0003-r3): `RatedLumens > 0`. Glow is derived from the
  shared `RideParameters.LightGlowFraction`. Invalid ratings throw
  `DomainValidationException`.
- Private state: `_level` ∈ [0, 1], the share of the rating currently emitted.
- Public getters: `Level`, `Lumens => _level * Rating.RatedLumens`, `Rating`.
- One intent-revealing step mutator: `Advance(double targetLevel, double dt)`. This
  follows the existing `AdvancePhysics` convention rather than a `SetX`, because
  advancing a simulation step *is* the intent (adr-0003-r2). It validates
  `targetLevel ∈ [0, 1]` and `dt > 0`.

Ownership:

| Owner | Fixtures | Rating |
| --- | --- | --- |
| `Gondola` | `Light` | 1 000 lm |
| `Hub` | `Light`, `ArmLights[4]` (index = gondola index) | 2 500 lm, 2 500 lm |
| `GreatMill` | `CrownLight`, `ArmLights[4]` (index = hub index) | 5 000 lm, 4 000 lm |

*Alternative:* a separate `LightRig` aggregate holding all 41 lights. Rejected: it
duplicates the mill → hub → gondola tree that the parts already model, and
propagation would have to walk that tree from outside.

### D2. Intensity is a gondola concept

`Gondola.Intensity => Math.Clamp(Math.Sqrt(ForwardG² + LateralG²) / MaxGForce, 0, 1)`.
This is the shared signal the `passenger-happiness` request also needs, so it becomes
a public domain property rather than lighting-private maths. Propagation is computed
from the owners down, with no stored intermediate state:

```
hub.Intensity  = max(g.Intensity for g in hub.Gondolas)
mill.Intensity = max(h.Intensity for h in mill.Hubs)
```

### D3. Target level per lifecycle state

```
                     ┌ 0                                    Idle
targetLevel(i) =     ┤ glow                                 Loading, Safe, Offloading
                     └ glow + (1 − glow) · i                Started, Stopping, EmergencyStop
glow = LightGlowFraction = 0.15
```

`Ride` decides the mode, because it owns the lifecycle, and passes it down as a small
enum (`LightingMode { Off, Glow, Reactive }`). In `Glow` mode the intensity is ignored,
which also avoids reading stale G values outside physics.

### D4. Asymmetric first-order envelope

```
τ   = target > level ? LightAttackSeconds : LightDecaySeconds
level += (target − level) · (1 − e^(−dt/τ))
```

with `LightAttackSeconds = 0.1` and `LightDecaySeconds = 0.8`. The step is exact for a
constant target over `dt`, unconditionally stable, and cannot overshoot, so `level`
stays within [0, 1] without clamping. It uses only `Math.Exp` on `double`: it's
deterministic for a given runtime and platform, like the rest of the physics.

**Flash-safety derivation.** A flash is a ≥ 20 %-of-rating fall followed by a ≥ 20 %
rise. The worst-case input is a full-swing square wave between glow (0.15) and 1.0. A
sweep of 0.25 to 30 Hz at the 1/120 s tick gives at most **2 flashes per second** (at
≈ 1.35 Hz) with τ_decay = 0.8 s. With τ_decay = 0.5 s it reaches 3 (at ≈ 2.4 Hz). Above
roughly 2 Hz the slow decay can't fall 20 % within a half-period, so the light simply
stays bright. `docs/06-ride-lighting.md` records this derivation, and a test sweeps the
same input (D7).

*Alternative:* a symmetric envelope or a moving average. Rejected: you need a slow
decay for flash safety, but a symmetric slow envelope makes G peaks lag and look
sluggish.

### D5. Lights advance every tick, after physics

`Ride.Advance` gains a final step (after the `Offloading → Idle` transition, so a ride
that has just gone idle starts fading the same tick):

```
5. _mill.AdvanceLights(ModeFor(_state), dt)
     → mill:  CrownLight ← mill.Intensity; ArmLights[h] ← hub[h].Intensity
     → hub:   Light ← hub.Intensity;       ArmLights[g] ← gondola[g].Intensity
     → gondola: Light ← gondola.Intensity
```

This runs in every state, including `Idle`, so the rig fades to dark after offloading.

### D6. Telemetry shape (additive)

| Record | Added members |
| --- | --- |
| `MillTelemetry` | `double CrownLightLumens`, `IReadOnlyList<double> ArmLightLumens` (4, by hub index) |
| `HubTelemetry` | `double LightLumens`, `IReadOnlyList<double> ArmLightLumens` (4, by gondola index) |
| `GondolaTelemetry` | `double LightLumens` |

New members are appended at the end of each positional record, so existing JSON
properties are untouched. Ratings are not streamed: they are constants, mirrored in a
frontend `LIGHT_RATINGS` constant next to the telemetry models, with a test asserting
the 87 000 lm total, as the spec requires.

### D7. Testing (xUnit v3, no FluentAssertions)

- `LightTests`: rise and fall time constants, no overshoot, bounds, validation, and
  `LightRating` validation.
- `GondolaIntensityTests`: the combined-axes, half-G and clamp scenarios from the spec.
- `RideLightingTests`: 41 fixtures, max propagation, state floors (settled values
  150 lm / 575 lm / 750 lm), the fade to dark on `Offloading → Idle`, emergency stop
  staying reactive, and deterministic replay (two rides, same steps, identical lumens).
- `LightFlashSafetyTests`: the D4 sweep. It drives `Light.Advance` with a square-wave
  target from 0.25 to 30 Hz and asserts at most 3 flashes in any one-second window.
- Telemetry tests extended for the new fields. Line coverage of the module stays
  ≥ 80 %.

### D8. Frontend

- **Models** (`ride.models.ts`): add a `light: { lumens, level }` to the mill crown,
  each mill arm, hub, hub arm and gondola, mapped from the stream DTO. `level` is
  `lumens / rating`.
- **Visualization**: one shared emissive material per fixture type is not enough,
  because each fixture differs. Each fixture mesh gets its own cloned
  `MeshStandardMaterial` with `emissive` warm white and `emissiveIntensity = level`,
  updated per telemetry frame. There are no `PointLight`s: 41 dynamic point lights in a
  forward renderer cost far more than emissive materials and add nothing operators
  need. The canvas stays `aria-hidden`.
- **Panel** (`panels/lights-panel/`): standalone, `OnPush`, signal inputs. Placed at
  the bottom of the left rail, as decided in explore. This is the first PrimeNG
  adoption. Use `p-progressbar` per row (the value is a percentage of the rating, the
  label is lumens) and a PrimeNG disclosure for the hub rows (`p-accordion` or a
  toggle button with `aria-expanded`; confirm the API and a11y with the `primeng` MCP
  server during apply). The total row reads "Total output 38 400 lm". Live-region
  behaviour follows the existing panels' polite pattern. Values are rounded to whole
  lumens so assistive technology isn't flooded with noise.
- Tests: Vitest for the mapping and the panel, plus a `lights-panel.a11y.spec.ts` axe
  spec.

## Risks / Trade-offs

- [The left rail is labelled `Controls` but this panel is read-only telemetry] →
  Accepted, because the user chose the left rail so that the right rail stays free for
  the Rider Experience panel. The panel gets its own `aria-label="Ride lights"`
  region, so landmark navigation stays clear.
- [Payload growth: 41 doubles per frame at 30 Hz] → About 1 kB/s extra JSON. That's
  negligible next to the existing seat data.
- [`passenger-safety` touches the same tick and telemetry] → Both are additive. Whoever
  lands second rebases the `Ride.Advance` step order and the record parameter lists.
- [Stale G outside physics] → `Glow` and `Off` modes ignore intensity (D3).
- [First PrimeNG component sets a precedent] → Configure theming once in `app.config`
  via `providePrimeNG` with `@primeuix/themes`, following the `primeng` MCP setup guide,
  so later panels reuse it.

## Migration Plan

Additive only. Old clients ignore the new JSON fields. Rollback is a plain revert. The
change depends on `telemetry-dto-layout` having merged into `development`.
