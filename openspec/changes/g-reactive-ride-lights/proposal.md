# Proposal

## Why

A carnival ride is sold by its lights as much as by its motion. The digital twin
already computes the G-forces every gondola's riders feel, but nothing on the ride
expresses them. Modelling a G-reactive light rig as ride hardware makes the twin's
intensity visible on the ride itself: lights brighten where the ride hits hard and fade
when it calms down. Because the rig is part of the domain, its output is a real,
deterministic and testable quantity (lumens) that operators can read on the dashboard
and that a future `Controller` lighting programme can build on.

## What Changes

- **New light rig on every moving part**: 41 fixtures in total. One on the Great Mill
  crown, one on each of the 4 mill arms, one on each of the 4 hubs, one on each of the
  16 hub arms and one on each of the 16 gondolas.
- **G-reactive output**: a gondola's light follows the total horizontal G its riders
  feel, normalised against the ride's maximum allowed G-force. That intensity
  propagates up the rig by **maximum**: a hub arm follows its gondola, a hub follows
  its hardest-hit gondola, a mill arm follows its hub, and the mill crown follows the
  hardest-hit gondola on the ride.
- **Lifecycle-aware floor**: dark while the ride is `Idle`, a steady glow while
  `Loading`, `Safe` and `Offloading`, and glow-plus-G-reactive while `Started`,
  `Stopping` and `EmergencyStop`.
- **Smoothed, flash-safe output**: brightness rises quickly and fades slowly, so free
  gondola swing never makes the rig flicker or flash more than three times per second.
- **Per-type lumen ratings**: gondola 1 000 lm, hub arm 2 500 lm, hub 2 500 lm, mill
  arm 4 000 lm and mill crown 5 000 lm. All constants live in `RideParameters` and are
  derived in a new `docs/06-ride-lighting.md`.
- **Telemetry**: every frame carries the lumen output of all 41 fixtures. This is an
  additive change to the payload.
- **Dashboard**: the 3D visualization makes each fixture glow according to its output,
  and a new PrimeNG **Ride lights** panel at the bottom of the left rail shows the
  output per mill and hub (gondolas expandable per hub) plus the rig's total.

Depends on `telemetry-dto-layout` landing first.

## Capabilities

### New Capabilities

- `ride-lighting`: the ride's light rig (fixtures, G-reactive intensity, propagation up
  the rig, lifecycle floors, smoothing and flash safety, lumen ratings).

### Modified Capabilities

- `ride-telemetry-streaming`: the full telemetry payload adds every fixture's lumen
  output.
- `ride-telemetry-panels`: adds the Ride lights panel.
- `ride-dashboard-layout`: the central visualization reflects each fixture's light
  output.

## Impact

- **DigitalTwin domain**: `Gondola`, `Hub` and `GreatMill` gain owned lights, `Ride`
  advances the rig every tick in every state, and `RideParameters` gains the lighting
  constants.
- **DigitalTwin.Abstractions**: `MillTelemetry`, `HubTelemetry` and `GondolaTelemetry`
  (under `DataTransferObjects/` after `telemetry-dto-layout`) gain lumen fields.
- **Docs**: a new `docs/06-ride-lighting.md`, plus updates to
  `appendix-parameters.md` and `docs/README.md`.
- **Angular app**: telemetry models and SSE mapping, `ride-visualization.ts`, and a new
  lights panel. This is the first PrimeNG component in the app.
- **Tests**: domain unit tests for the rig, telemetry tests, and Vitest + axe specs for
  the panel and the visualization.
- **No new endpoints, no new modules, no cross-module messaging, no new packages.**
- **Related**: `passenger-happiness` (feature request) needs the same normalised
  intensity signal; `passenger-safety` may later override the rig on an emergency stop.
