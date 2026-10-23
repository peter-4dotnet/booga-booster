import { provideHttpClient, withFetch } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import axe from 'axe-core';

import { QUEUE_SOURCE } from '../queue/data/queue-source';
import { createQueueStatus, FakeQueueSource } from '../queue/testing/fake-queue-source';
import { WEATHER_SOURCE } from '../weather/data/weather-source';
import { FakeWeatherSource, createConditions } from '../weather/testing/fake-weather-source';
import { RIDE_TELEMETRY_SOURCE } from './data/ride-telemetry-source';
import { RideTelemetryDto } from './models/ride.models';
import { RideDashboard } from './ride-dashboard';
import {
  createGondolas,
  createTelemetry,
  FakeRideTelemetrySource,
} from './testing/fake-ride-telemetry-source';

const TELEMETRY_URL = '/api/ride/telemetry';

/** Renders the dashboard against fake sources, with a started ride. */
async function renderDashboard() {
  const source = new FakeRideTelemetrySource();
  source.setTelemetry(createTelemetry({ state: 'started', gondolas: createGondolas(10, true) }));
  const weather = new FakeWeatherSource();
  weather.setStatus('ready');
  weather.setConditions(createConditions());
  const queue = new FakeQueueSource();
  queue.setStatus('ready');
  queue.setQueue(createQueueStatus());
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withFetch()),
      provideHttpClientTesting(),
      { provide: RIDE_TELEMETRY_SOURCE, useValue: source },
      { provide: WEATHER_SOURCE, useValue: weather },
      { provide: QUEUE_SOURCE, useValue: queue },
    ],
  });

  const fixture = TestBed.createComponent(RideDashboard);

  const http = TestBed.inject(HttpTestingController);
  const dto: RideTelemetryDto = {
    state: 'Started',
    availableTransitions: ['Stopping', 'EmergencyStop'],
  };
  http.expectOne(TELEMETRY_URL).flush(dto);

  fixture.detectChanges();
  await fixture.whenStable();
  return { fixture, host: fixture.nativeElement as HTMLElement };
}

async function violations(host: HTMLElement) {
  const results = await axe.run(host, {
    resultTypes: ['violations'],
    // Contrast needs real layout/colour, unavailable in jsdom.
    rules: { 'color-contrast': { enabled: false } },
  });
  return results.violations;
}

/**
 * Automated accessibility audit. jsdom cannot compute layout or colour, so the
 * colour-contrast rule is exercised separately against the design tokens; every
 * structural WCAG A/AA rule axe supports runs here.
 */
describe('RideDashboard accessibility', () => {
  it('has no AXE violations', async () => {
    const { host } = await renderDashboard();

    expect(await violations(host)).toEqual([]);
  });

  it('shows the Ride lights panel at the bottom of the left rail', async () => {
    const { host } = await renderDashboard();

    const rail = host.querySelector('section[aria-label="Controls"]');
    const lastPanel = rail?.querySelector(':scope > .panel:last-child');
    expect(lastPanel?.querySelector('bb-lights-panel h2')?.textContent?.trim()).toBe('Ride lights');
  });

  it('has no AXE violations with a Ride lights hub row expanded', async () => {
    const { fixture, host } = await renderDashboard();

    (host.querySelector('button[aria-controls="lights-hub-1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(await violations(host)).toEqual([]);
  });
});
