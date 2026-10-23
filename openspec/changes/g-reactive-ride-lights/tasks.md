# Tasks

## 0. Preconditions

- [x] 0.1 Confirm `telemetry-dto-layout` is merged into `development` and the telemetry records live in `DigitalTwin.Abstractions/DataTransferObjects/`, by checking that the folder exists on the branch base

## 1. Lighting specification docs

- [x] 1.1 Write `docs/06-ride-lighting.md`: fixtures and ratings, gondola intensity, max propagation, lifecycle floors, the asymmetric envelope, and the flash-safety derivation (D4 sweep result); verify it is linked from `docs/README.md`
- [x] 1.2 Add a "Lighting" table to `docs/appendix-parameters.md` (five ratings, glow fraction 0.15, attack 0.1 s, decay 0.8 s, total 87 000 lm, with a sanity check); verify the values match the doc from 1.1

## 2. Light domain type

- [x] 2.1 Add the lighting constants to `RideParameters` (ratings per type, `LightGlowFraction`, `LightAttackSeconds`, `LightDecaySeconds`), each with an XML doc pointing to `docs/06-ride-lighting.md`; verify the build is green
- [x] 2.2 Add the `LightRating` value object with validation, plus `LightRatingTests`; verify the tests pass
- [x] 2.3 Add `Light` with `Level`, `Lumens` and `Advance(targetLevel, dt)` per design D1 and D4, plus `LightTests` (rise faster than fall, time constants, no overshoot, bounds, argument validation); verify the tests pass
- [x] 2.4 Add `LightFlashSafetyTests`: a square-wave target sweep from 0.25 to 30 Hz at 1/120 s, asserting ≤ 3 flashes in any one-second window; verify the test passes, and that it fails if `LightDecaySeconds` is temporarily set to 0.3

## 3. Rig on the ride parts

- [x] 3.1 Add `Gondola.Intensity` (combined horizontal G / `MaxGForce`, clamped) and its own `Light`, plus `GondolaIntensityTests` for the spec's half-G, beyond-max and combined-axes scenarios; verify the tests pass
- [x] 3.2 Add the `LightingMode` enum (`Off`, `Glow`, `Reactive`) and the target-level function from design D3, with tests for each mode; verify the tests pass
- [x] 3.3 Give `Hub` its own `Light` and 4 `ArmLights` plus `Hub.Intensity` (max of its gondolas), and `GreatMill` its `CrownLight` and 4 `ArmLights` plus `Intensity`; add `AdvanceLights(mode, dt)` down the tree per design D5, with tests for 41 fixtures and max propagation (the spec's "one hard-hit gondola" and "crown follows" scenarios); verify the tests pass
- [x] 3.4 Call `_mill.AdvanceLights(...)` as the last step of `Ride.Advance` in every state, and add `RideLightingTests` (settled 0 lm idle, 150/750 lm loading, 575 lm at intensity 0.5 when started, emergency stop reactive, gradual fade on `Offloading → Idle`, deterministic replay); verify the tests pass and the existing physics tests stay green

## 4. Telemetry

- [x] 4.1 Append the light fields from design D6 to `MillTelemetry`, `HubTelemetry` and `GondolaTelemetry` and fill them in each `ToTelemetry()`; extend `TelemetryAndCommandTests` and `RideTelemetryStreamTests` to assert all 41 values and their ordering; verify the tests pass
- [x] 4.2 Run `dotnet test BoogaBooster.slnx` and the `test-coverage` skill on `FourDotnet.BoogaBooster.DigitalTwin`; verify the suite is green and line coverage is ≥ 80 % and not lower than before

## 5. Frontend models

- [x] 5.1 Extend the stream DTO interfaces and mapping in `ride.models.ts` with the light fields, and add a `LIGHT_RATINGS` constant; extend `ride.models.spec.ts` for the mapping, `level = lumens / rating`, and the 87 000 lm total; verify `npx vitest run src/app/ride-dashboard/models/ride.models.spec.ts` passes
- [x] 5.2 Update the simulated and fake telemetry sources to produce light values, so dev mode and existing specs keep working; verify the existing frontend specs pass

## 6. Visualization

- [x] 6.1 Give each of the 41 fixture meshes in `ride-visualization.ts` its own emissive material driven by its fixture's `level` per frame (design D8), keeping the canvas `aria-hidden`; extend `ride-visualization.spec.ts` to assert the material intensities follow a frame; verify the spec passes

## 7. Ride lights panel (first PrimeNG component)

- [x] 7.1 Consult the `primeng` MCP server (`get_setup`, `get_component` for ProgressBar and the disclosure component), then configure `providePrimeNG` with an `@primeuix/themes` preset in `app.config.ts`; verify the app builds with `npm run build`
- [x] 7.2 Build `panels/lights-panel/` (standalone, `OnPush`, `input()` signals): mill and hub rows with `p-progressbar` and lumen labels, expandable hub rows with gondola bars, the total row, and a `Ride lights` region label; add `lights-panel.spec.ts` covering the spec's live-output, expand (mouse and keyboard), total and dark-ride scenarios; verify the spec passes
- [x] 7.3 Add `lights-panel.a11y.spec.ts` (axe, WCAG AA: accessible names with "lm", the expanded state exposed); verify it reports no violations, and run `primeng` `validate_usage` on the template
- [x] 7.4 Place the panel at the bottom of the left rail in `ride-dashboard.html`, and extend `ride-dashboard.a11y.spec.ts`; verify the dashboard axe spec still reports no violations

## 8. Integration

- [x] 8.1 Run `dotnet build BoogaBooster.slnx`, `dotnet test BoogaBooster.slnx` and `npm test`; verify all are green
- [ ] 8.2 Run the AppHost, start a ride with passengers, and confirm in the dashboard that the rig glows while loading, brightens with G while running, fades on stop and goes dark at idle

## Workflow follow-up

- Archive the change on `development` after the PR merges (`/opsx:archive g-reactive-ride-lights`).
- Possible follow-ups: an emergency-stop full-brightness override (with `passenger-safety`), an Aspire light-output gauge, and `Controller` lighting programmes.
