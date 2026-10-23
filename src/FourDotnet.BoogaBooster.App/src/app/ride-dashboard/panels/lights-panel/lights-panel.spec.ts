import { TestBed } from '@angular/core/testing';

import { LIGHT_RATINGS, RideTelemetry, fixtureLight } from '../../models/ride.models';
import { createTelemetry } from '../../testing/fake-ride-telemetry-source';
import { LightsPanel, formatLumens } from './lights-panel';

/** Telemetry with the mill crown at 2 500 lm, hub 1 at 1 000 lm and hub 1's gondolas lit. */
function litTelemetry(): RideTelemetry {
  const telemetry = createTelemetry();
  return {
    ...telemetry,
    mill: { ...telemetry.mill, crownLight: fixtureLight(2500, LIGHT_RATINGS.millCrown) },
    hubs: telemetry.hubs.map((hub) =>
      hub.id === 1 ? { ...hub, light: fixtureLight(1000, LIGHT_RATINGS.hub) } : hub,
    ),
    gondolas: telemetry.gondolas.map((gondola) =>
      gondola.id <= 4
        ? { ...gondola, light: fixtureLight(gondola.id * 100, LIGHT_RATINGS.gondola) }
        : gondola,
    ),
  };
}

function render(telemetry: RideTelemetry) {
  const fixture = TestBed.createComponent(LightsPanel);
  fixture.componentRef.setInput('mill', telemetry.mill);
  fixture.componentRef.setInput('hubs', telemetry.hubs);
  fixture.componentRef.setInput('gondolas', telemetry.gondolas);
  fixture.detectChanges();
  return { fixture, host: fixture.nativeElement as HTMLElement };
}

/** The progress bar with the given accessible name. */
function bar(host: HTMLElement, name: string): HTMLElement | null {
  return host.querySelector(`[role="progressbar"][aria-label="${name}"]`);
}

/** Normalizes spacing so `2 500 lm` matches however it is grouped. */
function text(element: Element | null | undefined): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

function hubToggle(host: HTMLElement, hubId: number): HTMLButtonElement {
  return host.querySelector(`button[aria-controls="lights-hub-${hubId}"]`) as HTMLButtonElement;
}

describe('LightsPanel', () => {
  it('is a region labelled Ride lights', () => {
    const { host } = render(createTelemetry());
    const heading = host.querySelector('h2');
    const section = host.querySelector('section');

    expect(text(heading)).toBe('Ride lights');
    expect(section?.getAttribute('aria-labelledby')).toBe(heading?.id);
  });

  it('shows the mill and each hub fixture as a bar with its output in lumens', () => {
    const { host } = render(litTelemetry());

    const mill = bar(host, 'Mill light');
    expect(mill?.getAttribute('aria-valuenow')).toBe('50');
    expect(mill?.getAttribute('aria-valuetext')).toBe('2 500 lm');
    expect(text(mill?.closest('li')?.querySelector('.value'))).toBe('2 500 lm');

    const hub1 = bar(host, 'Hub 1 light');
    expect(hub1?.getAttribute('aria-valuenow')).toBe('40');
    expect(hub1?.getAttribute('aria-valuetext')).toBe('1 000 lm');

    for (const hubId of [2, 3, 4]) {
      expect(bar(host, `Hub ${hubId} light`)).not.toBeNull();
    }
  });

  it('keeps hub rows collapsed until expanded', () => {
    const { host } = render(litTelemetry());

    expect(hubToggle(host, 1).getAttribute('aria-expanded')).toBe('false');
    expect(bar(host, 'Gondola 1 light')).toBeNull();
  });

  it('expands a hub row with the mouse to show its four gondola fixtures', () => {
    const { fixture, host } = render(litTelemetry());

    hubToggle(host, 1).click();
    fixture.detectChanges();

    expect(hubToggle(host, 1).getAttribute('aria-expanded')).toBe('true');
    const gondolaBars = host.querySelectorAll('#lights-hub-1 [role="progressbar"]');
    expect(gondolaBars).toHaveLength(4);
    expect(bar(host, 'Gondola 3 light')?.getAttribute('aria-valuetext')).toBe('300 lm');
    expect(bar(host, 'Gondola 3 light')?.getAttribute('aria-valuenow')).toBe('30');
    // Other hubs stay collapsed.
    expect(hubToggle(host, 2).getAttribute('aria-expanded')).toBe('false');
    expect(bar(host, 'Gondola 5 light')).toBeNull();

    hubToggle(host, 1).click();
    fixture.detectChanges();
    expect(hubToggle(host, 1).getAttribute('aria-expanded')).toBe('false');
    expect(bar(host, 'Gondola 1 light')).toBeNull();
  });

  it('expands with the keyboard: the toggle is a focusable native button controlling the row', () => {
    const { fixture, host } = render(litTelemetry());
    const toggle = hubToggle(host, 2);

    // A native <button> is activated by Enter and Space; jsdom doesn't
    // synthesize that activation, so the test asserts the semantics that
    // guarantee it and then activates it as the browser would.
    expect(toggle.tagName).toBe('BUTTON');
    expect(toggle.type).toBe('button');
    expect(toggle.disabled).toBe(false);
    expect(toggle.tabIndex).toBeGreaterThanOrEqual(0);
    expect(host.querySelector(`#${toggle.getAttribute('aria-controls')}`)).not.toBeNull();

    toggle.focus();
    expect(document.activeElement).toBe(toggle);
    toggle.click();
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(host.querySelectorAll('#lights-hub-2 [role="progressbar"]')).toHaveLength(4);
  });

  it('shows the total output of all 41 fixtures', () => {
    const { host } = render(litTelemetry());

    // 2 500 (crown) + 1 000 (hub 1) + 100 + 200 + 300 + 400 (hub 1 gondolas).
    expect(text(host.querySelector('.total'))).toBe('Total output 4 500 lm');
    expect(host.querySelector('.total')?.getAttribute('aria-live')).toBe('polite');
  });

  it('shows a dark ride as empty bars reading 0 lm and stays visible', () => {
    const { fixture, host } = render(createTelemetry());
    hubToggle(host, 1).click();
    fixture.detectChanges();

    const bars = Array.from(host.querySelectorAll('[role="progressbar"]'));
    expect(bars).toHaveLength(1 + 4 + 4);
    bars.forEach((element) => {
      expect(element.getAttribute('aria-valuenow')).toBe('0');
      expect(element.getAttribute('aria-valuetext')).toBe('0 lm');
    });
    expect(text(host.querySelector('.total'))).toBe('Total output 0 lm');
    expect(host.querySelector('section')).not.toBeNull();
  });
});

describe('formatLumens', () => {
  it('rounds to whole lumens and groups thousands', () => {
    expect(formatLumens(0)).toBe('0 lm');
    expect(formatLumens(574.6)).toBe('575 lm');
    expect(formatLumens(2500)).toBe('2 500 lm');
    expect(formatLumens(87000)).toBe('87 000 lm');
  });

  it('reads a non-finite output as 0 lm', () => {
    expect(formatLumens(Number.NaN)).toBe('0 lm');
  });
});
