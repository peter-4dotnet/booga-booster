# Proposal

## Why

The DigitalTwin telemetry records (`RideTelemetry`, `MillTelemetry`, `HubTelemetry`,
`GondolaTelemetry`, `SeatTelemetry`) sit at the root of
`FourDotnet.BoogaBooster.DigitalTwin.Abstractions`, which violates the mandated DTO
layout (`<Module>.Abstractions/DataTransferObjects/...`, see the `dto-organization`
skill). The upcoming
`g-reactive-ride-lights` change extends every one of these records; moving them first,
as a pure refactor, keeps that feature's diff free of file moves and lets reviewers
check the move and the feature separately.

## What Changes

- Move the five telemetry records into the `DataTransferObjects` namespace and folder
  of `FourDotnet.BoogaBooster.DigitalTwin.Abstractions`. They are shared by two
  features of the same module (`GetRideTelemetry` and `StreamRideTelemetry`), so they
  live directly under `DataTransferObjects`, not in one feature's folder.
- Update every `using` and reference in the DigitalTwin module and its tests.
- No type is renamed, no property is added, removed or reordered, and the JSON wire
  format of `GET /ride/telemetry` and `GET /ride/telemetry/stream` is unchanged.
Out of scope: the inline request records in `DigitalTwinEndpoints`, the Queue DTOs and
the Weather request records. They remain listed as known discrepancies.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

None. This is a pure refactor with no behavioural change (`skip_specs: true`).

## Impact

- `src/DigitalTwin/FourDotnet.BoogaBooster.DigitalTwin.Abstractions/`: five files move
  into `DataTransferObjects/`.
- `src/DigitalTwin/FourDotnet.BoogaBooster.DigitalTwin/`: `using` updates in the
  application, domain, feature, endpoint and observability code.
- `src/Tests/FourDotnet.BoogaBooster.DigitalTwin.Tests/`: `using` updates.
- No other module references `DigitalTwin.Abstractions` today.
- Frontend: none (the wire format is unchanged).
- `CLAUDE.md` and `.github/copilot-instructions.md`: none. Their "Known style-guide
  discrepancies" list never named the telemetry records.
