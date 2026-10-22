import { TestBed } from '@angular/core/testing';

import { Gondola, HUB_COUNT, Hub, LIGHT_RATINGS, fixtureLight } from '../models/ride.models';
import { createGondolas, createTelemetry } from '../testing/fake-ride-telemetry-source';
import { GondolaPanel } from './gondola-panel/gondola-panel';
import { SecurityPanel } from './security-panel/security-panel';
import { SpeedPanel } from './speed-panel/speed-panel';

describe('SecurityPanel', () => {
  it('shows a load overview row with passenger count and security word', () => {
    const fixture = TestBed.createComponent(SecurityPanel);
    fixture.componentRef.setInput('passengers', 24);
    fixture.componentRef.setInput('securityState', 'unsecured');
    fixture.componentRef.setInput('totalKg', 1512);
    fixture.componentRef.setInput('loadBalance', 'safe');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Load:');
    expect(host.textContent).toContain('24');
    expect(host.textContent).toContain('passengers');
    expect(host.textContent).toContain('unsafe');
    expect(host.textContent).toContain('Weight:');
    expect(host.textContent).toContain('1512');
    expect(host.textContent).toContain('kilos');
    expect(host.textContent).toContain('safe');
  });

  it('singularizes the passenger count and reflects a safe/secured load', () => {
    const fixture = TestBed.createComponent(SecurityPanel);
    fixture.componentRef.setInput('passengers', 1);
    fixture.componentRef.setInput('securityState', 'secured');
    fixture.componentRef.setInput('totalKg', 70);
    fixture.componentRef.setInput('loadBalance', 'unsafe');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('1 passenger');
    expect(host.textContent).not.toContain('1 passengers');
    expect(host.querySelector('.row[data-state="safe"]')).not.toBeNull();
    expect(host.querySelector('.row[data-state="unsafe"]')).not.toBeNull();
  });
});

describe('SpeedPanel', () => {
  it('shows the mill speed and each hub speed independently with units', () => {
    const hubs: Hub[] = Array.from({ length: HUB_COUNT }, (_, i) => ({
      ...createTelemetry().hubs[i],
      id: i + 1,
      power: 50,
      direction: 'forward',
      speedRpm: (i + 1) * 3,
    }));
    const fixture = TestBed.createComponent(SpeedPanel);
    fixture.componentRef.setInput('mill', {
      power: 60,
      direction: 'forward',
      speedRpm: 4.5,
    });
    fixture.componentRef.setInput('hubs', hubs);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('4.50 rpm');
    expect(host.querySelectorAll('.hub').length).toBe(HUB_COUNT);
    expect(host.textContent).toContain('12.00 rpm');
  });
});

describe('GondolaPanel', () => {
  it('renders all 16 gondolas, each as two seat squares, with signed g-forces', () => {
    const gondolas: Gondola[] = createGondolas(16).map((g, i) => ({
      ...g,
      gForce: { vertical: i % 2 === 0 ? 2 : -1, lateral: i % 2 === 0 ? -0.5 : 0.5 },
    }));
    const fixture = TestBed.createComponent(GondolaPanel);
    fixture.componentRef.setInput('gondolas', gondolas);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelectorAll('.card').length).toBe(16);
    expect(host.querySelectorAll('.seat').length).toBe(32);
    expect(host.textContent).toContain('+2.0 g');
    expect(host.textContent).toContain('-0.5 g');
    // Speed and rotation speed must never appear in this panel.
    expect(host.textContent).not.toContain('rpm');
  });

  it('constrains each card to a quarter-width flex-basis so 16 gondolas form a 4x4 grid', () => {
    // jsdom cannot compute real layout, so the 4-per-row constraint is verified
    // against the compiled component styles rather than measured geometry.
    const styles = (GondolaPanel as unknown as { ɵcmp: { styles: readonly string[] } }).ɵcmp.styles;
    const css = styles.join('\n');
    expect(css).toMatch(/flex-wrap:\s*wrap/);
    expect(css).toMatch(/flex:\s*0\s+1\s+calc\(25%/);
  });

  it('colours seats grey/red/green for empty/unsecured/secured and sums seat weight', () => {
    const gondolas = createGondolas(1, true); // gondola 1: seat 1 unsecured, seat 2 secured
    const fixture = TestBed.createComponent(GondolaPanel);
    fixture.componentRef.setInput('gondolas', gondolas);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const cards = host.querySelectorAll('.card');
    const occupiedCard = cards[0] as HTMLElement;
    const emptyCard = cards[1] as HTMLElement;

    expect(occupiedCard.querySelector('.seat[data-state="occupied-unsecured"]')).not.toBeNull();
    expect(occupiedCard.querySelector('.seat[data-state="secured"]')).not.toBeNull();
    expect(emptyCard.querySelectorAll('.seat[data-state="empty"]').length).toBe(2);

    // Gondola 1 has one seat at 70kg (unsecured) and one at 70kg (secured) = 140kg.
    expect(occupiedCard.textContent).toContain('140 kg');
    expect(emptyCard.textContent).toContain('0 kg');
  });

  it('exposes each seat state and every metric unit in accessible text, never colour alone', () => {
    const gondolas = createGondolas(1, true);
    const fixture = TestBed.createComponent(GondolaPanel);
    fixture.componentRef.setInput('gondolas', gondolas);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('left seat: occupied, not secured');
    expect(host.textContent).toContain('right seat: occupied, secured');
    expect(host.textContent).toContain('empty');

    const weightMetric = host.querySelector('.metric[aria-label*="weight"]');
    expect(weightMetric?.getAttribute('aria-label')).toContain('kg');
    const verticalMetric = host.querySelector('.metric[aria-label*="vertical"]');
    expect(verticalMetric?.getAttribute('aria-label')).toContain('g');
  });
});

describe('GondolaPanel severity', () => {
  /** A single-gondola fixture with a chosen weight and g-forces. */
  function gondola(id: number, weightKg: number, vertical: number, lateral: number): Gondola {
    return {
      id,
      seats: [
        { id: 1, state: 'secured', occupiedKg: weightKg },
        { id: 2, state: 'empty', occupiedKg: 0 },
      ],
      gForce: { vertical, lateral },
      angleDegrees: 0,
      light: fixtureLight(0, LIGHT_RATINGS.gondola),
    };
  }

  function render(gondolas: Gondola[]): HTMLElement {
    const fixture = TestBed.createComponent(GondolaPanel);
    fixture.componentRef.setInput('gondolas', gondolas);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const weightLevel = (card: Element) =>
    card.querySelector('.metric[aria-label*="weight"]')?.getAttribute('data-level');
  const verticalLevel = (card: Element) =>
    card.querySelector('.metric[aria-label*="vertical"]')?.getAttribute('data-level');
  const lateralLevel = (card: Element) =>
    card.querySelector('.metric[aria-label*="lateral"]')?.getAttribute('data-level');

  it('marks weight over 250 as warn (orange) and over 300 as alert (red), inclusive limits stay normal', () => {
    const cards = render([
      gondola(1, 250, 0, 0), // at the warn limit -> still normal
      gondola(2, 260, 0, 0), // over 250 -> warn
      gondola(3, 300, 0, 0), // at the alert limit -> warn, not alert
      gondola(4, 320, 0, 0), // over 300 -> alert
    ]).querySelectorAll('.card');

    expect(weightLevel(cards[0])).toBe('normal');
    expect(weightLevel(cards[1])).toBe('warn');
    expect(weightLevel(cards[2])).toBe('warn');
    expect(weightLevel(cards[3])).toBe('alert');
  });

  it('marks vertical g beyond ±4.5 and lateral g beyond ±2 as alert, limits themselves stay normal', () => {
    const cards = render([
      gondola(1, 0, 4.5, 2), // exactly at both limits -> normal
      gondola(2, 0, 4.6, 0), // vertical over +4.5 -> alert
      gondola(3, 0, -4.6, 0), // vertical under -4.5 -> alert
      gondola(4, 0, 0, 2.1), // lateral over +2 -> alert
      gondola(5, 0, 0, -2.1), // lateral under -2 -> alert
    ]).querySelectorAll('.card');

    expect(verticalLevel(cards[0])).toBe('normal');
    expect(lateralLevel(cards[0])).toBe('normal');
    expect(verticalLevel(cards[1])).toBe('alert');
    expect(verticalLevel(cards[2])).toBe('alert');
    expect(lateralLevel(cards[3])).toBe('alert');
    expect(lateralLevel(cards[4])).toBe('alert');
  });

  it('spells out the over-limit status in the accessible name, never colour alone', () => {
    const host = render([gondola(1, 320, 4.6, 0)]);
    expect(
      host.querySelector('.metric[aria-label*="weight"]')?.getAttribute('aria-label'),
    ).toContain('over safe limit');
    expect(
      host.querySelector('.metric[aria-label*="vertical"]')?.getAttribute('aria-label'),
    ).toContain('over safe limit');
  });
});
