# Spec Delta

## MODIFIED Requirements

### Requirement: Full telemetry payload

Each streamed frame SHALL be a complete ride snapshot, not a partial delta. It SHALL include, per the `RideTelemetry` contract: the ride lifecycle state; the total count of boarded passengers currently on the ride; the mill's commanded power, sensed RPM, total and passenger load, imbalance and balance/overload flags; each of the four hubs' power, RPM and load; and each of the sixteen gondolas' brake state, yaw angle, RPM, forward and lateral G, load, and per-seat weight, restraint state, whether the seat is occupied, and whether its restraint is secured. It SHALL also include the current light output in lumens of every fixture of the ride's light rig: the mill crown and each of the four mill arms with the mill, each hub and each of its four hub arms with that hub, and each gondola's own fixture with that gondola.

#### Scenario: Frame carries mill and hub engine readings

- **WHEN** a telemetry frame is emitted while the ride is running
- **THEN** it contains the mill's power and RPM
- **AND** it contains the power and RPM of all four hubs

#### Scenario: Frame carries cart and seat state

- **WHEN** a telemetry frame is emitted while the ride is running
- **THEN** each gondola's brake, angle, RPM, forward/lateral G and load are present
- **AND** each seat's measured weight and restraint state are present
- **AND** each seat reports whether it is occupied and whether its restraint is secured

#### Scenario: Frame carries the boarded-passenger count

- **WHEN** a telemetry frame is emitted while passengers are on the ride
- **THEN** the frame carries the total count of boarded passengers
- **AND** the count equals the number of occupied seats across all gondolas

#### Scenario: Frame carries the light rig's output

- **WHEN** a telemetry frame is emitted in any active lifecycle state
- **THEN** it carries the lumen output of the mill crown and of the four mill arms, ordered by hub index
- **AND** each hub carries its own lumen output and that of its four hub arms, ordered by gondola index
- **AND** each gondola carries the lumen output of its own fixture

#### Scenario: Light fields are additive

- **WHEN** a client written before the light rig existed reads a telemetry frame
- **THEN** every field it already relied on is still present with unchanged name, type and meaning
