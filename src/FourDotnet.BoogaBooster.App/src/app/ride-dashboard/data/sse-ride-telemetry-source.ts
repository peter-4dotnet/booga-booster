import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, InjectionToken, Signal, inject, signal } from '@angular/core';

import {
  GONDOLAS_PER_HUB,
  GONDOLA_COUNT,
  Gondola,
  HUB_COUNT,
  LIGHT_RATINGS,
  MotorDirection,
  RideCommand,
  RideTelemetry,
  RideTelemetryStreamDto,
  SEATS_PER_GONDOLA,
  Seat,
  clampPower,
  fixtureLight,
  fixtureLights,
  mapRideTelemetry,
  toRideStateName,
} from '../models/ride.models';
import { RideTelemetrySource } from './ride-telemetry-source';

/** Relative API base; the dev-server proxy forwards `/api` to the backend. */
const TELEMETRY_STREAM_URL = '/api/ride/telemetry/stream';
const MAIN_POWER_URL = '/api/ride/main-power';
const HUB_POWER_URL = '/api/ride/hub-power';
const MAIN_DIRECTION_URL = '/api/ride/main-direction';
const HUB_DIRECTION_URL = '/api/ride/hub-direction';
const BRAKE_URL = '/api/ride/brake';
const STATE_URL = '/api/ride/state';
const ENGINE_BRAKE_URL = '/api/ride/engine-brake';

/**
 * The minimal surface of the browser `EventSource` this source depends on.
 * jsdom (the Vitest test environment) has no native `EventSource`, so
 * depending on this narrow interface — rather than the global type directly —
 * lets specs supply a fake without needing a real one.
 */
export interface EventSourceLike {
  onmessage: ((event: MessageEvent) => void) | null;
  addEventListener(type: string, listener: (event: MessageEvent) => void): void;
  close(): void;
}

/**
 * DI seam that opens the `EventSource` connected to the telemetry stream.
 * The default factory constructs a real `EventSource`; specs override this
 * token with a fake factory so `SseRideTelemetrySource` never touches the
 * real, jsdom-unavailable global.
 */
export const EVENT_SOURCE_FACTORY = new InjectionToken<(url: string) => EventSourceLike>(
  'EVENT_SOURCE_FACTORY',
  { providedIn: 'root', factory: () => (url: string) => new EventSource(url) },
);

/**
 * The ride's at-rest telemetry snapshot: idle, every motor stopped, every
 * gondola brake engaged, every seat empty, no passengers boarded. Used both
 * to seed {@link SseRideTelemetrySource} before its first frame arrives and
 * by specs that need the same baseline.
 */
export function atRestTelemetry(): RideTelemetry {
  const emptySeats = (): Seat[] =>
    Array.from({ length: SEATS_PER_GONDOLA }, (_, s) => ({
      id: s + 1,
      state: 'empty',
      occupiedKg: 0,
    }));

  const gondolas: Gondola[] = Array.from({ length: GONDOLA_COUNT }, (_, g) => ({
    id: g + 1,
    seats: emptySeats(),
    gForce: { vertical: 0, lateral: 0 },
    angleDegrees: 0,
    light: fixtureLight(0, LIGHT_RATINGS.gondola),
  }));

  return {
    state: 'idle',
    availableTransitions: ['loading'],
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
      direction: 'forward',
      speedRpm: 0,
      light: fixtureLight(0, LIGHT_RATINGS.hub),
      armLights: fixtureLights(undefined, GONDOLAS_PER_HUB, LIGHT_RATINGS.hubArm),
    })),
    gondolas,
    gondolaBrakeEngaged: true,
    brakesEngaged: false,
    boardedPassengerCount: 0,
  };
}

/**
 * {@link RideTelemetrySource} backed by the backend's SSE telemetry feed.
 *
 * Frames arrive from the server throughout `Loading`/`Safe`/`Started`/
 * `Stopping`/`Offloading`/`EmergencyStop` — i.e. whenever passengers may be
 * boarding, riding, or disembarking — but stay silent while `Idle`. To keep
 * the operator controls responsive at rest — where a plain "post and wait
 * for the next frame" would leave the sliders snapped back to their
 * last-known value — every command is also applied optimistically to the
 * local `telemetry` signal. A subsequent real frame simply overwrites the
 * optimistic value.
 */
@Injectable({ providedIn: 'root' })
export class SseRideTelemetrySource implements RideTelemetrySource {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly createEventSource = inject(EVENT_SOURCE_FACTORY);

  private readonly telemetrySignal = signal<RideTelemetry>(atRestTelemetry());

  readonly telemetry: Signal<RideTelemetry> = this.telemetrySignal.asReadonly();

  constructor() {
    const eventSource = this.createEventSource(TELEMETRY_STREAM_URL);
    const onFrame = (event: MessageEvent): void => this.handleFrame(event);
    // The default (unnamed) event and the backend's named `ride-telemetry`
    // event carry the same payload; handling both is cheap and future-proof.
    eventSource.onmessage = onFrame;
    eventSource.addEventListener('ride-telemetry', onFrame);
    // `EventSource` reconnects automatically on transient failures; no
    // manual retry logic is needed here.
    this.destroyRef.onDestroy(() => eventSource.close());
  }

  applyCommand(command: RideCommand): void {
    switch (command.kind) {
      case 'set-mill-power':
        this.setMillPower(clampPower(command.value));
        break;
      case 'set-hub-power':
        this.setHubPower(clampPower(command.value));
        break;
      case 'set-mill-direction':
        this.setMillDirection(command.direction);
        break;
      case 'set-hub-direction':
        this.setHubDirection(command.direction);
        break;
      case 'set-gondola-brake':
        this.setGondolaBrake(command.engaged);
        break;
      case 'request-state-transition':
        this.http.post(STATE_URL, { state: toRideStateName(command.state) }).subscribe();
        break;
      case 'brake-engines':
        this.brakeEngines(command.engaged);
        break;
    }
  }

  private setMillPower(percent: number): void {
    this.http.post(MAIN_POWER_URL, { percent }).subscribe();
    // Optimistic echo: reflects the commanded power immediately so the
    // slider doesn't snap back while the ride is idle and no frames arrive.
    this.telemetrySignal.update((current) => ({
      ...current,
      mill: { ...current.mill, power: percent },
    }));
  }

  private setHubPower(percent: number): void {
    this.http.post(HUB_POWER_URL, { percent }).subscribe();
    this.telemetrySignal.update((current) => ({
      ...current,
      hubs: current.hubs.map((hub) => ({ ...hub, power: percent })),
    }));
  }

  private setMillDirection(direction: MotorDirection): void {
    this.http.post(MAIN_DIRECTION_URL, { direction }).subscribe();
    // Optimistic echo: reflects the commanded direction immediately so the
    // toggle stays responsive while the ride is idle and no frames arrive.
    this.telemetrySignal.update((current) => ({
      ...current,
      mill: { ...current.mill, direction },
    }));
  }

  private setHubDirection(direction: MotorDirection): void {
    this.http.post(HUB_DIRECTION_URL, { direction }).subscribe();
    this.telemetrySignal.update((current) => ({
      ...current,
      hubs: current.hubs.map((hub) => ({ ...hub, direction })),
    }));
  }

  private setGondolaBrake(engaged: boolean): void {
    // There is no global brake endpoint: the backend engages/releases one
    // gondola at a time, so every one of the 4 hubs x 4 gondolas is posted
    // individually.
    for (let hubIndex = 0; hubIndex < HUB_COUNT; hubIndex++) {
      for (let gondolaIndex = 0; gondolaIndex < GONDOLAS_PER_HUB; gondolaIndex++) {
        this.http
          .post(BRAKE_URL, { hubIndex, gondolaIndex, brake: engaged ? 'Engaged' : 'Released' })
          .subscribe();
      }
    }
    this.telemetrySignal.update((current) => ({ ...current, gondolaBrakeEngaged: engaged }));
  }

  private brakeEngines(engaged: boolean): void {
    this.http.post(ENGINE_BRAKE_URL, { engaged }).subscribe();
    // Optimistic echo: reflects the commanded brake state immediately so the
    // toggle and sliders don't wait for the next real frame to confirm it.
    // Mill and hub power are only zeroed while engaging — releasing the
    // brake leaves the commanded power at zero without restoring it (the
    // operator must command power again), matching the backend contract.
    this.telemetrySignal.update((current) => ({
      ...current,
      brakesEngaged: engaged,
      mill: engaged ? { ...current.mill, power: 0 } : current.mill,
      hubs: engaged ? current.hubs.map((hub) => ({ ...hub, power: 0 })) : current.hubs,
    }));
  }

  private handleFrame(event: MessageEvent): void {
    try {
      const parsed: unknown = JSON.parse(event.data as string);
      this.telemetrySignal.set(mapRideTelemetry(parsed as RideTelemetryStreamDto));
    } catch {
      // A malformed frame keeps the previous snapshot rather than crashing
      // the feed; the next well-formed frame recovers it.
    }
  }
}
