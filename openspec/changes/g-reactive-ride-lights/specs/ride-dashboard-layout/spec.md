# Spec Delta

## ADDED Requirements

### Requirement: Visualization shows the ride's lights

The central ride visualization SHALL render a visible light on each of the 41 fixtures, at the part it belongs to. Each light's brightness SHALL follow that fixture's latest reported output as a share of its rating, so a dark fixture shows no glow and a fixture at its rating glows at full brightness. Because the Ride lights panel conveys the same information, the visualization SHALL remain decorative to assistive technology.

#### Scenario: Brightness follows output

- **WHEN** a telemetry frame reports one gondola's fixture at its rating and another's at 0 lm
- **THEN** the first gondola visibly glows at full brightness and the second shows no glow

#### Scenario: Lights move with their parts

- **WHEN** the ride is rotating
- **THEN** each light stays attached to the mill crown, arm, hub or gondola it belongs to

#### Scenario: Visualization stays decorative

- **WHEN** an AXE audit is run against the dashboard with the lights rendered
- **THEN** no violations are reported and the visualization is still hidden from assistive technology
