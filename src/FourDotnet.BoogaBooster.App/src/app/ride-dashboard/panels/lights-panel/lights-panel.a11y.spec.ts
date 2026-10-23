import { TestBed } from '@angular/core/testing';
import axe from 'axe-core';

import { LIGHT_RATINGS, RideTelemetry, fixtureLight } from '../../models/ride.models';
import { createTelemetry } from '../../testing/fake-ride-telemetry-source';
import { LightsPanel } from './lights-panel';

function render(telemetry: RideTelemetry) {
  const fixture = TestBed.createComponent(LightsPanel);
  fixture.componentRef.setInput('mill', telemetry.mill);
  fixture.componentRef.setInput('hubs', telemetry.hubs);
  fixture.componentRef.setInput('gondolas', telemetry.gondolas);
  fixture.detectChanges();
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
 * Automated accessibility audit of the Ride lights panel, collapsed and with a
 * hub expanded, plus the accessible names and values the spec requires.
 */
describe('LightsPanel accessibility', () => {
  it('has no AXE violations with every hub collapsed', async () => {
    const { fixture, host } = render(createTelemetry());
    await fixture.whenStable();

    expect(await violations(host)).toEqual([]);
  });

  it('has no AXE violations with a hub expanded', async () => {
    const telemetry = createTelemetry();
    const { fixture, host } = render({
      ...telemetry,
      mill: { ...telemetry.mill, crownLight: fixtureLight(2500, LIGHT_RATINGS.millCrown) },
    });
    (host.querySelector('button[aria-controls="lights-hub-1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(await violations(host)).toEqual([]);
  });

  it('names every bar after its part and gives it a value in lm', () => {
    const { fixture, host } = render(createTelemetry());
    host
      .querySelectorAll<HTMLButtonElement>('button[aria-expanded]')
      .forEach((toggle) => toggle.click());
    fixture.detectChanges();

    const bars = Array.from(host.querySelectorAll('.bar[role="progressbar"]'));
    expect(bars).toHaveLength(1 + 4 + 16);
    bars.forEach((bar) => {
      expect(bar.getAttribute('aria-label')).toMatch(/^(Mill|Hub \d|Gondola \d+) light$/);
      expect(bar.getAttribute('aria-valuetext')).toMatch(/ lm$/);
    });
  });

  it('exposes each hub row’s expanded state on its toggle', () => {
    const { fixture, host } = render(createTelemetry());
    const toggle = host.querySelector('button[aria-controls="lights-hub-3"]') as HTMLButtonElement;

    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
  });
});
