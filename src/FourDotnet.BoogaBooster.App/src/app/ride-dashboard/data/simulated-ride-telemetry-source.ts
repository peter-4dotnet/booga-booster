import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';

import { RideTelemetrySource } from './ride-telemetry-source';
import {
  FixtureLight,
  GONDOLAS_PER_HUB,
  GONDOLA_COUNT,
  Gondola,
  HUB_COUNT,
  LIGHT_RATINGS,
  MotorDirection,
  RideCommand,
  RideState,
  RideTelemetry,
  SEATS_PER_GONDOLA,
  Seat,
  clampPower,
  fixtureLight,
} from '../models/ride.models';

/** How often the simulator advances its values, in milliseconds. */
const TICK_MS = 500;

/** Rotation speed of the mill at full power, in rpm. */
const MAX_MILL_RPM = 8;

/** Rotation speed of a hub at full power, in rpm. */
const MAX_HUB_RPM = 20;

/** Fraction of the gap toward the target speed closed each tick. */
const SPEED_EASING = 0.25;

/** Lightest plausible occupied-seat load, in kg. */
const MIN_SEAT_KG = 30;

/** Heaviest plausible occupied-seat load, in kg. */
const MAX_SEAT_KG = 150;

/** The ride's maximum allowed G; mirrors the backend's `RideParameters.MaxGForce`. */
const MAX_G_FORCE = 4.5;

/** Share of its rating a fixture glows at; mirrors `RideParameters.LightGlowFraction`. */
const LIGHT_GLOW_FRACTION = 0.15;

/**
 * A fixture's light for a given G `intensity` in `[0, 1]`, following the
 * backend's lighting model (`docs/06-ride-lighting.md`) without its smoothing:
 * dark while idle, glow plus the scaled intensity while running.
 */
function simulatedLight(running: boolean, intensity: number, rating: number): FixtureLight {
  const level = running ? LIGHT_GLOW_FRACTION + (1 - LIGHT_GLOW_FRACTION) * intensity : 0;
  return fixtureLight(level * rating, rating);
}

interface CommandState {
  millPower: number;
  millDirection: MotorDirection;
  hubPower: number;
  hubDirection: MotorDirection;
  gondolaBrakeEngaged: boolean;
  brakesEngaged: boolean;
}

/**
 * Produces plausible, continuously-moving ride telemetry with no backend.
 *
 * Commanded power/direction are echoed into the telemetry immediately; sensed
 * speeds ease toward their target over successive ticks, hub speeds jitter with
 * simulated load, g-forces follow the speeds, and seat security occasionally
 * flips so the panels demonstrate live updates.
 */
@Injectable({ providedIn: 'root' })
export class SimulatedRideTelemetrySource implements RideTelemetrySource {
  private readonly destroyRef = inject(DestroyRef);

  private readonly commands: CommandState = {
    millPower: 0,
    millDirection: 'forward',
    hubPower: 0,
    hubDirection: 'forward',
    gondolaBrakeEngaged: false,
    brakesEngaged: false,
  };

  private millSpeed = 0;
  private readonly hubSpeeds = new Array<number>(HUB_COUNT).fill(0);
  private seatStates = this.createInitialSeatStates();
  // Set once per occupied seat at startup and held constant while occupied; the
  // seat-jostle logic only ever flips secured/unsecured, never the load itself.
  private readonly seatWeightsKg = this.seatStates.map((state) =>
    state === 'empty' ? 0 : randomSeatKg(),
  );

  private readonly telemetrySignal = signal<RideTelemetry>(this.buildTelemetry());

  readonly telemetry = this.telemetrySignal.asReadonly();

  constructor() {
    interval(TICK_MS)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.tick());
  }

  applyCommand(command: RideCommand): void {
    switch (command.kind) {
      case 'set-mill-power':
        this.commands.millPower = clampPower(command.value);
        break;
      case 'set-hub-power':
        this.commands.hubPower = clampPower(command.value);
        break;
      case 'set-mill-direction':
        this.commands.millDirection = command.direction;
        break;
      case 'set-hub-direction':
        this.commands.hubDirection = command.direction;
        break;
      case 'set-gondola-brake':
        this.commands.gondolaBrakeEngaged = command.engaged;
        break;
      case 'request-state-transition':
        // Lifecycle transitions are backend-authoritative (see
        // `RideLifecycleService`); the simulator only tracks physics state,
        // so this is a no-op here.
        break;
      case 'brake-engines':
        this.commands.brakesEngaged = command.engaged;
        if (command.engaged) {
          this.commands.millPower = 0;
          this.commands.hubPower = 0;
        }
        break;
    }
    // Echo the command immediately so controls and status reflect it at once.
    this.telemetrySignal.set(this.buildTelemetry());
  }

  private tick(): void {
    const millTarget = (this.commands.millPower / 100) * MAX_MILL_RPM;
    this.millSpeed += (millTarget - this.millSpeed) * SPEED_EASING;
    if (Math.abs(this.millSpeed) < 0.01) {
      this.millSpeed = 0;
    }

    const hubTargetBase = (this.commands.hubPower / 100) * MAX_HUB_RPM;
    for (let i = 0; i < this.hubSpeeds.length; i++) {
      // Per-hub load jitter so hubs run at slightly different speeds.
      const load = hubTargetBase > 0 ? (Math.random() - 0.5) * 2 : 0;
      const target = Math.max(0, hubTargetBase + load);
      this.hubSpeeds[i] += (target - this.hubSpeeds[i]) * SPEED_EASING;
      if (this.hubSpeeds[i] < 0.01) {
        this.hubSpeeds[i] = 0;
      }
    }

    this.jostleSeats();
    this.telemetrySignal.set(this.buildTelemetry());
  }

  /** Occasionally flip a random occupied seat's secured/unsecured state. */
  private jostleSeats(): void {
    if (Math.random() > 0.3) {
      return;
    }
    const index = Math.floor(Math.random() * this.seatStates.length);
    const current = this.seatStates[index];
    if (current === 'secured') {
      this.seatStates[index] = 'occupied-unsecured';
    } else if (current === 'occupied-unsecured') {
      this.seatStates[index] = 'secured';
    }
  }

  private buildTelemetry(): RideTelemetry {
    const state = this.deriveState();
    const running = state === 'started';
    const intensities = this.gondolaIntensities();
    const gondolas = this.buildGondolas(running, intensities);
    const hubIntensity = (hub: number) =>
      Math.max(...intensities.slice(hub * GONDOLAS_PER_HUB, (hub + 1) * GONDOLAS_PER_HUB));

    return {
      state,
      // The simulator drives physics only; lifecycle transitions are
      // server-authoritative via `RideLifecycleService`.
      availableTransitions: [],
      mill: {
        power: this.commands.millPower,
        direction: this.commands.millDirection,
        speedRpm: round(this.millSpeed, 2),
        crownLight: simulatedLight(running, Math.max(...intensities), LIGHT_RATINGS.millCrown),
        armLights: Array.from({ length: HUB_COUNT }, (_, hub) =>
          simulatedLight(running, hubIntensity(hub), LIGHT_RATINGS.millArm),
        ),
      },
      hubs: this.hubSpeeds.map((speed, index) => ({
        id: index + 1,
        power: this.commands.hubPower,
        direction: this.commands.hubDirection,
        speedRpm: round(speed, 2),
        light: simulatedLight(running, hubIntensity(index), LIGHT_RATINGS.hub),
        armLights: Array.from({ length: GONDOLAS_PER_HUB }, (_, j) =>
          simulatedLight(running, intensities[index * GONDOLAS_PER_HUB + j], LIGHT_RATINGS.hubArm),
        ),
      })),
      gondolas,
      gondolaBrakeEngaged: this.commands.gondolaBrakeEngaged,
      brakesEngaged: this.commands.brakesEngaged,
    };
  }

  /** Each gondola's G as a share of the ride's maximum, in `[0, 1]`. */
  private gondolaIntensities(): number[] {
    return Array.from({ length: GONDOLA_COUNT }, (_, g) => {
      const { vertical, lateral } = this.gondolaGForce(g);
      return Math.min(1, Math.hypot(vertical, lateral) / MAX_G_FORCE);
    });
  }

  /**
   * Signs alternate per gondola and follow the motor directions so the panels
   * show both push-back/push-forward and left/right forces. Each gondola takes
   * the speed of the hub it is mounted on.
   */
  private gondolaGForce(g: number): { vertical: number; lateral: number } {
    const hubSpeed = this.hubSpeeds[Math.floor(g / GONDOLAS_PER_HUB)];
    const verticalSign = this.commands.hubDirection === 'forward' ? 1 : -1;
    const lateralSign = g % 2 === 0 ? 1 : -1;
    return {
      vertical: (hubSpeed / MAX_HUB_RPM) * 3 * verticalSign,
      lateral: (this.millSpeed / MAX_MILL_RPM) * 1.5 * lateralSign,
    };
  }

  private buildGondolas(running: boolean, intensities: readonly number[]): Gondola[] {
    const gondolas: Gondola[] = [];
    for (let g = 0; g < GONDOLA_COUNT; g++) {
      const seats: Seat[] = [];
      for (let s = 0; s < SEATS_PER_GONDOLA; s++) {
        const seatIndex = g * SEATS_PER_GONDOLA + s;
        seats.push({
          id: s + 1,
          state: this.seatStates[seatIndex],
          occupiedKg: this.seatWeightsKg[seatIndex],
        });
      }
      const { vertical, lateral } = this.gondolaGForce(g);
      gondolas.push({
        id: g + 1,
        seats,
        gForce: { vertical: round(vertical, 1), lateral: round(lateral, 1) },
        // Even angular spacing around the hub; not animated by the
        // simulator, which only drives the 3D rig from motor speed.
        angleDegrees: round((g % GONDOLAS_PER_HUB) * (360 / GONDOLAS_PER_HUB), 1),
        light: simulatedLight(running, intensities[g], LIGHT_RATINGS.gondola),
      });
    }
    return gondolas;
  }

  private deriveState(): RideState {
    return this.commands.millPower > 0 || this.commands.hubPower > 0 ? 'started' : 'idle';
  }

  /** Ten gondolas start fully occupied and secured (20 riders); rest empty. */
  private createInitialSeatStates(): Seat['state'][] {
    const states: Seat['state'][] = [];
    for (let g = 0; g < GONDOLA_COUNT; g++) {
      for (let s = 0; s < SEATS_PER_GONDOLA; s++) {
        states.push(g < 10 ? 'secured' : 'empty');
      }
    }
    return states;
  }
}

function round(value: number, decimals: number): number {
  const factor = 10 ** decimals;
  return Math.round(value * factor) / factor;
}

/** A plausible, whole-kilogram passenger weight in [MIN_SEAT_KG, MAX_SEAT_KG]. */
function randomSeatKg(): number {
  return Math.round(MIN_SEAT_KG + Math.random() * (MAX_SEAT_KG - MIN_SEAT_KG));
}
