# Spec Delta

## Purpose

Defines the ride's light rig: a fixture on every moving part whose lumen output follows the G-forces riders feel, scaled by the ride's lifecycle state and smoothed so it never flashes.

## ADDED Requirements

### Requirement: Every moving part carries a light fixture

The ride SHALL carry 41 light fixtures: one on the Great Mill crown, one on each of the 4 mill arms, one on each of the 4 hubs, one on each of the 16 hub arms and one on each of the 16 gondolas. Each fixture SHALL report its current output in lumens, from 0 up to and including its rated output.

#### Scenario: Fixture count

- **WHEN** the ride's light rig is inspected in any lifecycle state
- **THEN** it reports exactly 1 mill crown, 4 mill arm, 4 hub, 16 hub arm and 16 gondola fixtures

#### Scenario: Output stays within the rating

- **WHEN** any fixture's output is read at any moment
- **THEN** it is at least 0 lm and at most that fixture's rated output

### Requirement: Fixture ratings per part type

Each fixture SHALL have a rated (maximum) output by part type: gondola 1 000 lm, hub arm 2 500 lm, hub 2 500 lm, mill arm 4 000 lm and mill crown 5 000 lm. The rig's total rated output SHALL therefore be 87 000 lm.

#### Scenario: Rig at full output

- **WHEN** every fixture is at its rated output
- **THEN** the rig's combined output is 87 000 lm

### Requirement: Gondola intensity follows the felt G-force

A gondola's lighting intensity SHALL be the magnitude of the horizontal G its riders feel (the combination of its forward and lateral G), divided by the ride's maximum allowed G-force and clamped to the range 0 to 1.

#### Scenario: Half the allowed G

- **WHEN** a gondola's riders feel a combined horizontal G of half the maximum allowed G-force
- **THEN** that gondola's intensity is 0.5

#### Scenario: Beyond the allowed G

- **WHEN** a gondola's riders feel more than the maximum allowed G-force
- **THEN** that gondola's intensity is 1

#### Scenario: Combined axes

- **WHEN** a gondola has a forward G of 0.6 g and a lateral G of 0.8 g
- **THEN** its intensity is based on a combined G of 1.0 g

### Requirement: Intensity propagates up the rig by maximum

Each fixture above a gondola SHALL take the highest intensity among the gondolas it carries: a hub arm its own gondola, a hub its 4 gondolas, a mill arm the 4 gondolas of the hub on that arm, and the mill crown all 16 gondolas.

#### Scenario: One hard-hit gondola lights its branch

- **WHEN** one gondola on a hub has intensity 0.9 and the hub's other three gondolas have intensity 0.2
- **THEN** that hub's fixture and the mill arm carrying it take intensity 0.9
- **AND** the hub arm fixtures of the other three gondolas take intensity 0.2

#### Scenario: The crown follows the hardest-hit gondola

- **WHEN** the highest gondola intensity anywhere on the ride is 0.7
- **THEN** the mill crown takes intensity 0.7

### Requirement: Lifecycle state sets the lighting floor

The rig's target output SHALL depend on the ride's lifecycle state. In `Idle` every fixture SHALL target 0 lm. In `Loading`, `Safe` and `Offloading` every fixture SHALL target its glow output, 15 % of its rating, regardless of G. In `Started`, `Stopping` and `EmergencyStop` every fixture SHALL target its glow output plus the remaining 85 % of its rating scaled by its intensity.

#### Scenario: Idle ride is dark

- **WHEN** the ride has been `Idle` long enough for the lights to settle
- **THEN** every fixture outputs 0 lm

#### Scenario: Loading ride glows

- **WHEN** the ride has been `Loading` long enough for the lights to settle
- **THEN** every gondola fixture outputs 150 lm and the mill crown outputs 750 lm

#### Scenario: Running ride reacts to G

- **WHEN** the ride is `Started` and a gondola's intensity has been a steady 0.5 long enough for its light to settle
- **THEN** that gondola's fixture outputs 575 lm (150 lm glow plus 0.5 × 850 lm)

#### Scenario: Emergency stop stays G-reactive

- **WHEN** the ride enters `EmergencyStop`
- **THEN** each fixture keeps following its intensity on top of its glow, exactly as while `Started`

### Requirement: Light output is smoothed

A fixture's output SHALL move towards its target continuously over time and never jump to it. It SHALL rise towards a higher target quickly and fall towards a lower target slowly, so it fades out rather than cutting off. This includes the fade to dark when the ride returns to `Idle`.

#### Scenario: Rising is faster than falling

- **WHEN** a fixture's target rises by a given amount and, separately, falls by the same amount
- **THEN** its output covers the rise in less time than the fall

#### Scenario: Returning to idle fades out

- **WHEN** the ride goes from `Offloading` to `Idle`
- **THEN** each fixture's output decreases gradually to 0 lm over time instead of dropping to 0 lm in a single step

### Requirement: The rig never flashes

For any G-force input, no fixture SHALL complete more than three flashes in any one-second period. A flash is a fall in output of at least 20 % of the fixture's rating followed by a rise of at least 20 % of its rating, or the reverse.

#### Scenario: Rapidly oscillating G

- **WHEN** a gondola's G alternates between 0 and the maximum allowed G-force at 5 Hz for several seconds while the ride is `Started`
- **THEN** no fixture completes more than three flashes in any one-second period

### Requirement: Lighting is deterministic

The rig's output SHALL be a pure function of the ride's state and the fixed simulation steps taken. Two rides started from the same state and advanced with the same sequence of steps SHALL report identical outputs for every fixture.

#### Scenario: Replay produces identical lights

- **WHEN** two rides with identical state are advanced through the same sequence of simulation steps
- **THEN** every fixture reports the same output on both rides after every step
