# Tasks

## 1. Move the telemetry records

- [x] 1.1 `git mv` `RideTelemetry.cs`, `MillTelemetry.cs`, `HubTelemetry.cs`, `GondolaTelemetry.cs` and `SeatTelemetry.cs` into `src/DigitalTwin/FourDotnet.BoogaBooster.DigitalTwin.Abstractions/DataTransferObjects/`
- [x] 1.2 Change their namespace to `FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects`, with names and members unchanged
- [x] 1.3 Add `using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;` wherever the records are used: `IRideTelemetryProvider`, `Application/`, `Domain/`, `Features/`, `Endpoints/`, `Observability/` and `DigitalTwinModuleExtensions.cs`
- [x] 1.4 Update the `using` lines in `src/Tests/FourDotnet.BoogaBooster.DigitalTwin.Tests`

## 2. Verify

- [x] 2.1 `dotnet build BoogaBooster.slnx` is green with no new warnings
- [x] 2.2 `dotnet test BoogaBooster.slnx` is green, with no test changed apart from `using` lines
- [x] 2.3 Scan `DigitalTwin.Abstractions` with the `dto-organization` skill: no telemetry record remains at the project root, and folder matches namespace
- [x] 2.4 Line coverage of `FourDotnet.BoogaBooster.DigitalTwin` is unchanged (≥ 80 %)
