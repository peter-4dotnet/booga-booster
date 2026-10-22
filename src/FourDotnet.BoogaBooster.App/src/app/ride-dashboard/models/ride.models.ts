/**
 * Domain models for the ride dashboard.
 *
 * The ride is one central mill rotating 16 gondolas mounted on 4 hubs (4
 * gondolas per hub). Each hub spins independently and carries a fixed number of
 * seats per gondola. These plain types are the contract shared by the
 * ride-state service, the telemetry source (simulated today, a real backend
 * feed later) and the presentational panels.
 */

/**
 * Overall lifecycle state of the ride, backed by the server-side state
 * machine (see `RideLifecycleService`). Order matches the backend's
 * `RideState` enum index (`0=Idle` … `6=EmergencyStop`).
 */
export type RideState =
  'idle' | 'loading' | 'safe' | 'started' | 'stopping' | 'offloading' | 'emergency-stop';

/** Direction a motor drives its rotation. */
export type MotorDirection = 'forward' | 'reverse';

/** State of a single passenger seat. */
export type SeatState = 'empty' | 'occupied-unsecured' | 'secured';

/** Overall security roll-up across every occupied seat. */
export type SecurityState = 'secured' | 'unsecured';

/** Overall rotational load-balance roll-up across every gondola. */
export type LoadBalanceState = 'safe' | 'unsafe';

/** Number of gondolas arranged around the central mill. */
export const GONDOLA_COUNT = 16;

/** Number of independently-driven hubs the gondolas are mounted on. */
export const HUB_COUNT = 4;

/** Number of gondolas carried by each hub. */
export const GONDOLAS_PER_HUB = GONDOLA_COUNT / HUB_COUNT;

/** Fixed number of seats per gondola. */
export const SEATS_PER_GONDOLA = 2;

/** Lowest allowed motor power, in percent. */
export const MIN_POWER = 0;

/** Highest allowed motor power, in percent. */
export const MAX_POWER = 100;

/**
 * Maximum allowed normalized rotational imbalance (see {@link loadEccentricity})
 * before the ride is considered off-balance. An evenly-spread load, or two
 * equal loads seated opposite each other, stays well under this threshold; a
 * load concentrated on one side of the mill exceeds it.
 */
export const MAX_SAFE_ECCENTRICITY = 0.35;

/** A single passenger seat. */
export interface Seat {
  readonly id: number;
  readonly state: SeatState;
  /** Weight measured by the seat's load cell, in kg (0 when empty). */
  readonly occupiedKg: number;
}

/**
 * G-forces experienced by a gondola, in g. Both values are signed: a positive
 * vertical force pushes riders back/up, a negative one pushes forward/down; a
 * positive lateral force pushes one way, a negative one the other.
 */
export interface GForce {
  readonly vertical: number;
  readonly lateral: number;
}

/**
 * Rated (maximum) output of each light fixture type, in lumens. Mirrors the
 * backend's `RideParameters.*LightRatedLumens` (see `docs/06-ride-lighting.md`)
 * — **must stay in sync**. Ratings are constants, so they are not streamed.
 */
export const LIGHT_RATINGS = {
  millCrown: 5000,
  millArm: 4000,
  hub: 2500,
  hubArm: 2500,
  gondola: 1000,
} as const;

/** Combined rated output of all 41 fixtures of the light rig, in lumens (87 000 lm). */
export const LIGHT_RIG_RATED_LUMENS =
  LIGHT_RATINGS.millCrown +
  HUB_COUNT * (LIGHT_RATINGS.millArm + LIGHT_RATINGS.hub) +
  GONDOLA_COUNT * (LIGHT_RATINGS.hubArm + LIGHT_RATINGS.gondola);

/** The current output of one light fixture. */
export interface FixtureLight {
  /** Output in lumens, from 0 up to the fixture's rating. */
  readonly lumens: number;
  /** Output as a share of the fixture's rating, in `[0, 1]`. */
  readonly level: number;
}

/**
 * A fixture's light from its reported `lumens` and its `rating`: `level` is
 * `lumens / rating`, clamped into `[0, 1]`. A missing or non-finite value
 * (an older frame) reads as dark.
 */
export function fixtureLight(lumens: number | undefined, rating: number): FixtureLight {
  const value = Number.isFinite(lumens) ? Math.min(rating, Math.max(0, lumens as number)) : 0;
  return { lumens: value, level: value / rating };
}

/**
 * `count` fixture lights of one `rating`, taking each output from `lumens` by
 * index; missing entries (or a missing list) read as dark.
 */
export function fixtureLights(
  lumens: readonly number[] | undefined,
  count: number,
  rating: number,
): FixtureLight[] {
  return Array.from({ length: count }, (_, i) => fixtureLight(lumens?.[i], rating));
}

/** The central mill motor, its sensed rotation speed and its light fixtures. */
export interface Mill {
  readonly power: number;
  readonly direction: MotorDirection;
  readonly speedRpm: number;
  /** The fixture on the mill's crown. */
  readonly crownLight: FixtureLight;
  /** The four mill arm fixtures, ordered by the hub each arm carries. */
  readonly armLights: readonly FixtureLight[];
}

/** A single hub carrying four gondolas; hub speeds vary with load. */
export interface Hub {
  readonly id: number;
  readonly power: number;
  readonly direction: MotorDirection;
  readonly speedRpm: number;
  /** The hub's own fixture. */
  readonly light: FixtureLight;
  /** The hub's four arm fixtures, ordered by the gondola each arm carries. */
  readonly armLights: readonly FixtureLight[];
}

/** A gondola: the seats it carries, the g-forces it is experiencing and its light. */
export interface Gondola {
  readonly id: number;
  readonly seats: readonly Seat[];
  readonly gForce: GForce;
  /** Current angular position of the gondola around its hub, in degrees. */
  readonly angleDegrees: number;
  /** The gondola's own fixture. */
  readonly light: FixtureLight;
}

/** A full snapshot of ride telemetry. */
export interface RideTelemetry {
  readonly state: RideState;
  /** The operator-triggerable target states legal right now. */
  readonly availableTransitions: readonly RideState[];
  readonly mill: Mill;
  readonly hubs: readonly Hub[];
  readonly gondolas: readonly Gondola[];
  /** Whether the gondola brakes are engaged (pods held) or released (pods swing free). */
  readonly gondolaBrakeEngaged: boolean;
  /**
   * Whether the engine brake is engaged: drive power is cut and a strong
   * brake torque is applied to the mill and hubs, bringing the ride to a
   * complete stop within a couple of seconds.
   */
  readonly brakesEngaged: boolean;
  /**
   * Total number of occupied seats across all gondolas, as streamed by the
   * backend. Optional so older frames/fixtures without the field still type
   * check; consumers should fall back to counting non-`'empty'` seats when
   * it is absent (see `RideStateService.occupiedSeats`).
   */
  readonly boardedPassengerCount?: number;
}

/**
 * Raw wire shape of the lifecycle fields on `GET /api/ride/telemetry` that
 * {@link RideLifecycleService} cares about. `state`/`availableTransitions`
 * may arrive as numeric enum indices or as PascalCase names (System.Text.Json
 * default), so both are accepted here — see `toRideState`. The response also
 * carries `mill`/`hubs`/`gondolas`/`simulationTimeSeconds`/`isSafeToStart`/
 * `safetyReason`, which are not modelled here: the dashboard's physics
 * panels stay driven by the client simulator, not this feed.
 */
export interface RideTelemetryDto {
  readonly state: number | string;
  readonly availableTransitions: readonly (number | string)[];
}

/**
 * Highest power the central mill motor can draw, in watts. Mirrors the
 * backend's `RideParameters.MillMaxPowerWatts` — **must stay in sync** with
 * that value, since it is only used here to turn absolute watts back into a
 * percent for display/control.
 */
export const MILL_MAX_POWER_WATTS = 90000;

/**
 * Highest power a single hub motor can draw, in watts. Mirrors the backend's
 * `RideParameters.HubMaxPowerWatts` — **must stay in sync** with that value.
 */
export const HUB_MAX_POWER_WATTS = 15000;

/**
 * Raw wire shape of the central mill within a `RideTelemetry` stream frame.
 * `direction` may arrive as a numeric enum index or a name (see
 * `MotorDirection` on the backend), so both are accepted. `rpm` is signed:
 * negative while the mill runs in reverse.
 */
export interface RideTelemetryMillStreamDto {
  readonly powerWatts: number;
  readonly rpm: number;
  readonly direction: number | string;
  readonly loadKg: number;
  readonly passengerLoadKg: number;
  readonly imbalanceMillimeters: number;
  readonly isBalanced: boolean;
  readonly isOverloaded: boolean;
  /** Mill crown fixture output, in lumens. Absent on frames from before the light rig. */
  readonly crownLightLumens?: number;
  /** Mill arm fixture outputs, in lumens, by hub index. Absent on older frames. */
  readonly armLightLumens?: readonly number[];
}

/**
 * Raw wire shape of a single hub within a `RideTelemetry` stream frame.
 * `direction` may arrive as a numeric enum index or a name; `rpm` is signed.
 */
export interface RideTelemetryHubStreamDto {
  readonly index: number;
  readonly powerWatts: number;
  readonly rpm: number;
  readonly direction: number | string;
  readonly loadKg: number;
  /** Hub fixture output, in lumens. Absent on frames from before the light rig. */
  readonly lightLumens?: number;
  /** Hub arm fixture outputs, in lumens, by gondola index. Absent on older frames. */
  readonly armLightLumens?: readonly number[];
}

/**
 * Raw wire shape of a single seat within a `RideTelemetry` stream frame.
 * `position`/`restraint` may arrive as a numeric enum index or a name (see
 * `SeatPosition`/`RestraintState` on the backend), so both are accepted.
 * `isOccupied`/`isSecured` are the explicit occupancy/security flags the
 * backend now streams — see {@link seatState}.
 */
export interface RideTelemetrySeatStreamDto {
  readonly position: number | string;
  readonly occupiedKg: number;
  readonly restraint: number | string;
  readonly isOccupied: boolean;
  readonly isSecured: boolean;
}

/**
 * Raw wire shape of a single gondola within a `RideTelemetry` stream frame.
 * `brake` may arrive as a numeric enum index or a name (see
 * `GondolaBrakeState` on the backend), so both are accepted.
 */
export interface RideTelemetryGondolaStreamDto {
  readonly hubIndex: number;
  readonly index: number;
  readonly brake: number | string;
  readonly angleDegrees: number;
  readonly rpm: number;
  readonly lateralG: number;
  readonly forwardG: number;
  readonly loadKg: number;
  readonly isSafeToDispatch: boolean;
  readonly seats: readonly RideTelemetrySeatStreamDto[];
  /** Gondola fixture output, in lumens. Absent on frames from before the light rig. */
  readonly lightLumens?: number;
}

/**
 * Full raw wire shape of one `RideTelemetry` snapshot, shared by
 * `GET /api/ride/telemetry` and each `GET /api/ride/telemetry/stream` frame.
 * `state`/`availableTransitions` may arrive as numeric enum indices or as
 * PascalCase names (System.Text.Json default), so both are accepted — see
 * `toRideState`. `isSafeToStart`/`safetyReason`/`simulationTimeSeconds` are
 * not modelled on the frontend yet. `boardedPassengerCount` is the total
 * number of occupied seats across all 16 gondolas, computed server-side.
 * `brakesEngaged` is optional so older frames/fixtures without the field
 * still type check; {@link mapRideTelemetry} defaults it to `false` (brake
 * released) when absent.
 */
export interface RideTelemetryStreamDto {
  readonly state: number | string;
  readonly availableTransitions: readonly (number | string)[];
  readonly isSafeToStart: boolean;
  readonly safetyReason: number | string;
  readonly simulationTimeSeconds: number;
  readonly mill: RideTelemetryMillStreamDto;
  readonly hubs: readonly RideTelemetryHubStreamDto[];
  readonly gondolas: readonly RideTelemetryGondolaStreamDto[];
  readonly boardedPassengerCount: number;
  readonly brakesEngaged?: boolean;
}

/** The backend's `GondolaBrakeState` names in index order (`0=Engaged, 1=Released`). */
const GONDOLA_BRAKE_NAMES = ['Engaged', 'Released'] as const;

/** The backend's `RestraintState` names in index order (`0=Open, 1=Closed, 2=Secured`). */
const RESTRAINT_NAMES = ['Open', 'Closed', 'Secured'] as const;

/** The backend's `SeatPosition` names in index order (`0=Left, 1=Right`). */
const SEAT_POSITION_NAMES = ['Left', 'Right'] as const;

/** The backend's `MotorDirection` names in index order (`0=Forward, 1=Reverse`). */
const MOTOR_DIRECTION_NAMES = ['Forward', 'Reverse'] as const;

/**
 * Resolves a numeric-or-string wire enum value to its zero-based index, given
 * the enum's names in index order. String values are matched
 * case-insensitively; an unrecognized value falls back to index `0`.
 */
function enumIndexOf(value: number | string, names: readonly string[]): number {
  if (typeof value === 'number') {
    return value;
  }
  const index = names.findIndex((name) => name.toLowerCase() === value.toLowerCase());
  return index === -1 ? 0 : index;
}

/** Whether the wire `GondolaBrakeState` value represents the engaged state. */
function brakeEngaged(value: number | string): boolean {
  return enumIndexOf(value, GONDOLA_BRAKE_NAMES) === 0;
}

/** Zero-based seat position (`0=Left, 1=Right`) from the wire `SeatPosition` value. */
function seatPositionIndex(value: number | string): number {
  return enumIndexOf(value, SEAT_POSITION_NAMES);
}

/** The motor direction from the wire `MotorDirection` value (`0=Forward, 1=Reverse`). */
function motorDirection(value: number | string): MotorDirection {
  return enumIndexOf(value, MOTOR_DIRECTION_NAMES) === 1 ? 'reverse' : 'forward';
}

/**
 * A seat is `'empty'` only when it is not occupied; otherwise it is
 * `'secured'` when the seat is secured (preferring the explicit `isSecured`
 * flag, falling back to the wire `RestraintState` being `Secured`) and
 * `'occupied-unsecured'` otherwise.
 *
 * Occupancy prefers the explicit `isOccupied` flag the backend now streams;
 * when it is `undefined` (an older frame), this falls back to the previous
 * `occupiedKg > 0` heuristic.
 */
function seatState(
  occupiedKg: number,
  restraint: number | string,
  isOccupied?: boolean,
  isSecured?: boolean,
): SeatState {
  const occupied = isOccupied ?? occupiedKg > 0;
  if (!occupied) {
    return 'empty';
  }
  const secured = isSecured ?? enumIndexOf(restraint, RESTRAINT_NAMES) === 2;
  return secured ? 'secured' : 'occupied-unsecured';
}

/**
 * Lossy conversion: the backend reports absolute motor power in watts, but
 * the UI models power as a percent of a fixed maximum. Rounds to the nearest
 * whole percent and clamps into `[0, 100]`.
 */
function fromWatts(watts: number, maxWatts: number): number {
  return clampPower(Math.round((watts / maxWatts) * 100));
}

/** Rounds `value` to `decimals` decimal places (0 by default). */
function round(value: number, decimals = 0): number {
  const factor = 10 ** decimals;
  return Math.round(value * factor) / factor;
}

/**
 * Maps one raw `RideTelemetry` wire snapshot (from the SSE stream or the
 * plain `GET`) onto the frontend's normalized {@link RideTelemetry}.
 *
 * Several conversions are deliberately lossy — see the comments below — since
 * the frontend models motor power as a percent and reports only two axes of
 * g-force, while the backend reports absolute watts and named force axes.
 */
export function mapRideTelemetry(dto: RideTelemetryStreamDto): RideTelemetry {
  const gondolas = dto.gondolas.map((gondola) => ({
    id: gondola.hubIndex * GONDOLAS_PER_HUB + gondola.index + 1,
    angleDegrees: round(gondola.angleDegrees, 1),
    // The backend's `forwardG`/`lateralG` map onto the frontend's
    // `vertical`/`lateral` g-force axes respectively.
    gForce: { vertical: round(gondola.forwardG, 1), lateral: round(gondola.lateralG, 1) },
    seats: gondola.seats.map((seat) => ({
      id: seatPositionIndex(seat.position) + 1,
      occupiedKg: round(seat.occupiedKg),
      state: seatState(seat.occupiedKg, seat.restraint, seat.isOccupied, seat.isSecured),
    })),
    light: fixtureLight(gondola.lightLumens, LIGHT_RATINGS.gondola),
  }));

  // Prefer the backend's own tally; fall back to counting non-empty seats
  // for an older frame that doesn't carry `boardedPassengerCount`.
  const boardedPassengerCount =
    dto.boardedPassengerCount ??
    gondolas.reduce(
      (total, gondola) => total + gondola.seats.filter((seat) => seat.state !== 'empty').length,
      0,
    );

  return {
    state: toRideState(dto.state),
    availableTransitions: dto.availableTransitions.map(toRideState),
    mill: {
      power: fromWatts(dto.mill.powerWatts, MILL_MAX_POWER_WATTS),
      direction: motorDirection(dto.mill.direction),
      // Signed: negative while the mill runs in reverse.
      speedRpm: round(dto.mill.rpm, 2),
      // Older frames without the light rig read as a dark rig.
      crownLight: fixtureLight(dto.mill.crownLightLumens, LIGHT_RATINGS.millCrown),
      armLights: fixtureLights(dto.mill.armLightLumens, HUB_COUNT, LIGHT_RATINGS.millArm),
    },
    hubs: dto.hubs.map((hub) => ({
      id: hub.index + 1,
      power: fromWatts(hub.powerWatts, HUB_MAX_POWER_WATTS),
      direction: motorDirection(hub.direction),
      speedRpm: round(hub.rpm, 2),
      light: fixtureLight(hub.lightLumens, LIGHT_RATINGS.hub),
      armLights: fixtureLights(hub.armLightLumens, GONDOLAS_PER_HUB, LIGHT_RATINGS.hubArm),
    })),
    gondolas,
    boardedPassengerCount,
    // The backend engages/releases all gondola brakes together; the frontend
    // models one flag, so it is true only when every gondola reports engaged.
    gondolaBrakeEngaged: dto.gondolas.every((gondola) => brakeEngaged(gondola.brake)),
    // Defaults to released for an older frame that doesn't carry the field.
    brakesEngaged: dto.brakesEngaged ?? false,
  };
}

/** Set the central mill motor power (0–100). */
export interface SetMillPowerCommand {
  readonly kind: 'set-mill-power';
  readonly value: number;
}

/** Set the hub motor group power (0–100). */
export interface SetHubPowerCommand {
  readonly kind: 'set-hub-power';
  readonly value: number;
}

/** Set the central mill motor direction. */
export interface SetMillDirectionCommand {
  readonly kind: 'set-mill-direction';
  readonly direction: MotorDirection;
}

/** Set the hub motor group direction. */
export interface SetHubDirectionCommand {
  readonly kind: 'set-hub-direction';
  readonly direction: MotorDirection;
}

/** Engage or release the gondola brakes. */
export interface SetGondolaBrakeCommand {
  readonly kind: 'set-gondola-brake';
  readonly engaged: boolean;
}

/** Request a ride lifecycle transition. */
export interface RequestStateTransitionCommand {
  readonly kind: 'request-state-transition';
  readonly state: RideState;
}

/**
 * Engage or release the engine brake. Engaging cuts mill and hub power to
 * zero and applies a strong brake torque, bringing the ride to a fast,
 * complete stop; releasing leaves power at zero until commanded again.
 */
export interface BrakeEnginesCommand {
  readonly kind: 'brake-engines';
  readonly engaged: boolean;
}

/** Any operator command the ride-state service can dispatch. */
export type RideCommand =
  | SetMillPowerCommand
  | SetHubPowerCommand
  | SetMillDirectionCommand
  | SetHubDirectionCommand
  | SetGondolaBrakeCommand
  | RequestStateTransitionCommand
  | BrakeEnginesCommand;

/** Clamp a numeric value into the inclusive [min, max] range. */
export function clampPower(value: number, min = MIN_POWER, max = MAX_POWER): number {
  if (Number.isNaN(value)) {
    return min;
  }
  return Math.min(max, Math.max(min, value));
}

/** Total ride load, in kg: the sum of every seat's sensed weight. */
export function totalLoadKg(gondolas: readonly Gondola[]): number {
  return gondolas.reduce(
    (total, gondola) =>
      total + gondola.seats.reduce((seatTotal, seat) => seatTotal + seat.occupiedKg, 0),
    0,
  );
}

/**
 * Normalized rotational imbalance of the load around the central mill, in
 * `[0, 1]`. The 16 gondolas sit at even angular positions; for each gondola at
 * index `i` its angle is `(i / count) * 2π` and its weight is the sum of its
 * seats' `occupiedKg`. The eccentricity is the magnitude of the weight-vector
 * sum divided by the total weight — `0` for an evenly-spread (or empty) load,
 * approaching `1` as the load concentrates on one side of the mill.
 */
export function loadEccentricity(gondolas: readonly Gondola[]): number {
  let sumX = 0;
  let sumY = 0;
  let totalWeight = 0;

  gondolas.forEach((gondola, index) => {
    const weight = gondola.seats.reduce((seatTotal, seat) => seatTotal + seat.occupiedKg, 0);
    const angle = (index / gondolas.length) * 2 * Math.PI;
    sumX += weight * Math.cos(angle);
    sumY += weight * Math.sin(angle);
    totalWeight += weight;
  });

  if (totalWeight === 0) {
    return 0;
  }
  return Math.sqrt(sumX * sumX + sumY * sumY) / totalWeight;
}

/** Combined output of all 41 fixtures of the light rig, in lumens. */
export function totalLightLumens(telemetry: RideTelemetry): number {
  const sum = (lights: readonly FixtureLight[]) =>
    lights.reduce((total, light) => total + light.lumens, 0);

  return (
    telemetry.mill.crownLight.lumens +
    sum(telemetry.mill.armLights) +
    telemetry.hubs.reduce((total, hub) => total + hub.light.lumens + sum(hub.armLights), 0) +
    telemetry.gondolas.reduce((total, gondola) => total + gondola.light.lumens, 0)
  );
}

/** Safe only when the load's rotational eccentricity is within tolerance. */
export function loadBalanceState(gondolas: readonly Gondola[]): LoadBalanceState {
  return loadEccentricity(gondolas) <= MAX_SAFE_ECCENTRICITY ? 'safe' : 'unsafe';
}

/**
 * The seven lifecycle states in the backend `RideState` enum's index order
 * (`0=Idle` … `6=EmergencyStop`); used to map a numeric wire value and to
 * enumerate every state (e.g. for the transition button grid).
 */
export const RIDE_STATE_BY_INDEX: readonly RideState[] = [
  'idle',
  'loading',
  'safe',
  'started',
  'stopping',
  'offloading',
  'emergency-stop',
];

/** The backend's PascalCase name for each lifecycle state. */
const RIDE_STATE_NAMES: Readonly<Record<RideState, string>> = {
  idle: 'Idle',
  loading: 'Loading',
  safe: 'Safe',
  started: 'Started',
  stopping: 'Stopping',
  offloading: 'Offloading',
  'emergency-stop': 'EmergencyStop',
};

/** Human-readable label for a lifecycle state, e.g. `'emergency-stop'` → `'Emergency stop'`. */
const RIDE_STATE_LABELS: Readonly<Record<RideState, string>> = {
  idle: 'Idle',
  loading: 'Loading',
  safe: 'Safe',
  started: 'Started',
  stopping: 'Stopping',
  offloading: 'Offloading',
  'emergency-stop': 'Emergency stop',
};

/**
 * Maps the server's numeric or string `RideState` value onto the frontend
 * union. Accepts a numeric enum index or a PascalCase name, matched
 * case-insensitively; falls back to `'idle'` for an unrecognized value.
 */
export function toRideState(value: number | string): RideState {
  if (typeof value === 'number') {
    return RIDE_STATE_BY_INDEX[value] ?? 'idle';
  }
  const normalized = value.toLowerCase();
  return (
    RIDE_STATE_BY_INDEX.find((state) => RIDE_STATE_NAMES[state].toLowerCase() === normalized) ??
    'idle'
  );
}

/** Maps a frontend lifecycle state back onto the backend's PascalCase name. */
export function toRideStateName(state: RideState): string {
  return RIDE_STATE_NAMES[state];
}

/** Human-friendly ride-state label, e.g. `'emergency-stop'` → `'Emergency stop'`. */
export function rideStateLabel(state: RideState): string {
  return RIDE_STATE_LABELS[state];
}
