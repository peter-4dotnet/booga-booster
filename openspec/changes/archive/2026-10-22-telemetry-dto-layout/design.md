# Design

## Context

`FourDotnet.BoogaBooster.DigitalTwin.Abstractions` holds, at its root, five telemetry
records, six enums (`RideState`, `RideSafetyReason`, `MotorDirection`,
`GondolaBrakeState`, `RestraintState`, `SeatPosition`) and one interface
(`IRideTelemetryProvider`). The domain (`Gondola`, `Hub`, `GreatMill`, `Seat`, `Ride`)
builds the records in its `ToTelemetry()` methods. Two HTTP features consume them:
`GetRideTelemetry` (`GET /ride/telemetry`, via `IQueryHandler<GetRideTelemetryQuery,
RideTelemetry>`) and `StreamRideTelemetry` (`GET /ride/telemetry/stream`, via
`RideTelemetryStream`). No other module references `DigitalTwin.Abstractions`.

## Goals / Non-Goals

**Goals:**
- The telemetry records follow the `dto-organization` layout, with folder and namespace
  matching.
- The wire format is byte-for-byte identical before and after.

**Non-Goals:**
- Renaming the records to `<Feature>Response`.
- Moving the enums or `IRideTelemetryProvider`.
- Fixing the other known DTO discrepancies (inline endpoint requests, Queue, Weather).

## Decisions

### D1. Shared shapes live directly under `DataTransferObjects`

Target: `DigitalTwin.Abstractions/DataTransferObjects/<Record>.cs`, namespace
`FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects`.

The `dto-organization` skill places a shape reused by several features of the same
module directly under `DataTransferObjects`. `RideTelemetry` and its nested records are
the payload of both `GetRideTelemetry` and `StreamRideTelemetry`.

*Alternative:* `DataTransferObjects/GetRideTelemetry/GetRideTelemetryResponse`, with the
stream reusing it. This ties the stream to another feature's response type and renames
a type that the domain builds. Rejected.

### D2. Keep the record names

`RideTelemetry`, `MillTelemetry`, `HubTelemetry`, `GondolaTelemetry` and
`SeatTelemetry` are not one feature's request/response pair, so the
`<Feature>Request`/`<Feature>Response` naming rule doesn't apply. Keeping the names also
keeps this change a pure move. System.Text.Json serialises property names, not type
names, so the wire format can't change either way.

### D3. Enums and the provider interface stay at the root

The enums are shared contract vocabulary that the domain uses as well as the DTOs. They
are not data carriers. `IRideTelemetryProvider` is a service contract. Neither is a DTO
under the skill's definition, so both stay where they are.

### D4. Move with `git mv`, one commit

One `refactor(digital-twin)` commit moves the files (so git records renames) and
updates every `using`. No aliases or forwarding types are left behind.

## Risks / Trade-offs

- [The wire format changes by accident] → The existing telemetry and stream tests
  (`TelemetryAndCommandTests`, `RideTelemetryStreamTests`) assert the payload and must
  stay green unchanged, apart from `using` lines.
- [Merge conflicts with in-flight changes that touch telemetry (`passenger-safety`)] →
  The change is small and mechanical; whoever lands second re-applies the `using`
  updates.
