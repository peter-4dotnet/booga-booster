# Spec Delta

## ADDED Requirements

### Requirement: Ride lights panel

The dashboard SHALL show a Ride lights panel at the bottom of the left rail. It SHALL list the mill and each of the four hubs, each with a bar showing its fixture's output as a share of its rating and the output in lumens. Each hub row SHALL expand to show its four gondolas' fixtures the same way. The panel SHALL show the rig's total output in lumens.

#### Scenario: Live output per part

- **WHEN** a telemetry frame reports the mill crown at 2 500 lm and hub 1 at 1 000 lm
- **THEN** the mill row shows a half-full bar labelled 2 500 lm
- **AND** the hub 1 row shows a 40 % bar labelled 1 000 lm

#### Scenario: Expanding a hub

- **WHEN** the operator expands a hub row using the mouse or the keyboard
- **THEN** the four gondola fixtures of that hub are shown with their own bars and lumen values
- **AND** the row exposes its expanded or collapsed state to assistive technology

#### Scenario: Rig total

- **WHEN** a telemetry frame is received
- **THEN** the panel shows the sum of all 41 fixtures' output in lumens

#### Scenario: Dark ride

- **WHEN** every fixture outputs 0 lm
- **THEN** every bar is empty and every value reads 0 lm, and the panel stays visible

#### Scenario: Values are accessible

- **WHEN** the panel is rendered
- **THEN** every bar has an accessible name identifying its part and a value that includes the unit lm
- **AND** an AXE audit of the panel reports no violations
