import { signal } from '@angular/core';

import { RideTelemetrySource } from '../data/ride-telemetry-source';
import {
  GONDOLAS_PER_HUB,
  GONDOLA_COUNT,
  Gondola,
  HUB_COUNT,
  LIGHT_RATINGS,
  RideCommand,
  RideTelemetry,
  SEATS_PER_GONDOLA,
  Seat,
  SeatState,
  clampPower,
  fixtureLight,
  fixtureLights,
} from '../models/ride.models';

/**
 * Deterministic {@link RideTelemetrySource} for tests: no timers, no randomness.
 * Commands update the telemetry synchronously and every applied command is
 * recorded so specs can assert on what was dispatched.
 */
export class FakeRideTelemetrySource implements RideTelemetrySource {
  readonly commands: RideCommand[] = [];

  private readonly telemetrySignal = signal<RideTelemetry>(createTelemetry());

  readonly telemetry = this.telemetrySignal.asReadonly();

  applyCommand(command: RideCommand): void {
    this.commands.push(command);
    const current = this.telemetrySignal();
    switch (command.kind) {
      case 'set-mill-power':
        this.telemetrySignal.set({
          ...current,
          state: command.value > 0 ? 'started' : 'idle',
          mill: { ...current.mill, power: clampPower(command.value) },
        });
        break;
      case 'set-hub-power':
        this.telemetrySignal.set({
          ...current,
          hubs: current.hubs.map((hub) => ({ ...hub, power: clampPower(command.value) })),
        });
        break;
      case 'set-mill-direction':
        this.telemetrySignal.set({
          ...current,
          mill: { ...current.mill, direction: command.direction },
        });
        break;
      case 'set-hub-direction':
        this.telemetrySignal.set({
          ...current,
          hubs: current.hubs.map((hub) => ({ ...hub, direction: command.direction })),
        });
        break;
      case 'set-gondola-brake':
        this.telemetrySignal.set({ ...current, gondolaBrakeEngaged: command.engaged });
        break;
      case 'request-state-transition':
        this.telemetrySignal.set({ ...current, state: command.state });
        break;
      case 'brake-engines':
        this.telemetrySignal.set({
          ...current,
          brakesEngaged: command.engaged,
          mill: command.engaged ? { ...current.mill, power: 0 } : current.mill,
          hubs: command.engaged ? current.hubs.map((hub) => ({ ...hub, power: 0 })) : current.hubs,
        });
        break;
    }
  }

  /** Replace the telemetry snapshot outright (simulates an external update). */
  setTelemetry(telemetry: RideTelemetry): void {
    this.telemetrySignal.set(telemetry);
  }
}

/** Build a telemetry snapshot, overriding any fields for a specific scenario. */
export function createTelemetry(overrides: Partial<RideTelemetry> = {}): RideTelemetry {
  return {
    state: 'idle',
    availableTransitions: [],
    mill: {
      power: 0,
      direction: 'forward',
      speedRpm: 0,
      crownLight: fixtureLight(0, LIGHT_RATINGS.millCrown),
      armLights: fixtureLights(undefined, HUB_COUNT, LIGHT_RATINGS.millArm),
    },
    hubs: Array.from({ length: HUB_COUNT }, (_, i) => ({
      id: i + 1,
      power: 0,
      direction: 'forward' as const,
      speedRpm: 0,
      light: fixtureLight(0, LIGHT_RATINGS.hub),
      armLights: fixtureLights(undefined, GONDOLAS_PER_HUB, LIGHT_RATINGS.hubArm),
    })),
    gondolas: createGondolas(),
    gondolaBrakeEngaged: false,
    brakesEngaged: false,
    ...overrides,
  };
}

/** A fixed, plausible passenger weight used for occupied seats in fixtures. */
const FIXTURE_SEAT_KG = 70;

/** Build 16 gondolas; by default the first `occupiedGondolas` are secured. */
export function createGondolas(occupiedGondolas = 0, unsecuredSeat = false): Gondola[] {
  return Array.from({ length: GONDOLA_COUNT }, (_, g) => {
    const occupied = g < occupiedGondolas;
    const seats: Seat[] = Array.from({ length: SEATS_PER_GONDOLA }, (_, s) => {
      let state: SeatState = 'empty';
      if (occupied) {
        state = unsecuredSeat && g === 0 && s === 0 ? 'occupied-unsecured' : 'secured';
      }
      return { id: s + 1, state, occupiedKg: occupied ? FIXTURE_SEAT_KG : 0 };
    });
    return {
      id: g + 1,
      seats,
      gForce: { vertical: 0, lateral: 0 },
      angleDegrees: 0,
      light: fixtureLight(0, LIGHT_RATINGS.gondola),
    };
  });
}
