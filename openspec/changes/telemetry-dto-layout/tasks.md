# Tasks

## 1. Move the telemetry records

- [ ] 1.1 `git mv` `RideTelemetry.cs`, `MillTelemetry.cs`, `HubTelemetry.cs`, `GondolaTelemetry.cs` and `SeatTelemetry.cs` into `src/DigitalTwin/FourDotnet.BoogaBooster.DigitalTwin.Abstractions/DataTransferObjects/`
- [ ] 1.2 Change their namespace to `FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects`, with names and members unchanged
- [ ] 1.3 Add `using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;` wherever the records are used: `IRideTelemetryProvider`, `Application/`, `Domain/`, `Features/`, `Endpoints/`, `Observability/` and `DigitalTwinModuleExtensions.cs`
- [ ] 1.4 Update the `using` lines in `src/Tests/FourDotnet.BoogaBooster.DigitalTwin.Tests`

## 2. Verify

- [ ] 2.1 `dotnet build BoogaBooster.slnx` is green with no new warnings
- [ ] 2.2 `dotnet test BoogaBooster.slnx` is green, with no test changed apart from `using` lines
- [ ] 2.3 Scan `DigitalTwin.Abstractions` with the `dto-organization` skill: no telemetry record remains at the project root, and folder matches namespace
- [ ] 2.4 Line coverage of `FourDotnet.BoogaBooster.DigitalTwin` is unchanged (≥ 80 %)
