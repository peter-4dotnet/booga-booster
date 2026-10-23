import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { ButtonDirective } from 'primeng/button';
import { ProgressBar } from 'primeng/progressbar';

import {
  FixtureLight,
  GONDOLAS_PER_HUB,
  Gondola,
  Hub,
  Mill,
  totalLightLumens,
} from '../../models/ride.models';

/**
 * Formats an output as whole lumens with grouped thousands, e.g. `2 500 lm`.
 * Rounding to whole lumens keeps assistive technology from being flooded with
 * fractional changes.
 */
export function formatLumens(lumens: number): string {
  const whole = Math.round(Number.isFinite(lumens) ? lumens : 0);
  return `${whole.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ' ')} lm`;
}

/**
 * Bottom-left panel: the ride's light rig. One bar per mill and hub fixture,
 * showing its output as a share of its rating with the output in lumens; each
 * hub row expands to show its four gondolas' fixtures, and the rig's total
 * output (all 41 fixtures) is shown below. Only the total is a polite live
 * region, so the continuously changing bars don't flood a screen reader.
 */
@Component({
  selector: 'bb-lights-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, ProgressBar, ButtonDirective],
  template: `
    <section class="lights" aria-labelledby="lights-heading">
      <h2 id="lights-heading">Ride lights</h2>
      <ul class="rows">
        <li class="row">
          <span class="name">Mill</span>
          <ng-container
            *ngTemplateOutlet="bar; context: { $implicit: mill().crownLight, label: 'Mill light' }"
          />
        </li>
        @for (row of hubRows(); track row.hub.id) {
          <li class="row hub-row">
            <button
              pButton
              type="button"
              variant="text"
              severity="secondary"
              size="small"
              class="toggle"
              [attr.aria-expanded]="row.expanded"
              [attr.aria-controls]="'lights-hub-' + row.hub.id"
              (click)="toggle(row.hub.id)"
            >
              <span class="chevron" aria-hidden="true">{{ row.expanded ? '▾' : '▸' }}</span>
              Hub {{ row.hub.id }}
            </button>
            <ng-container
              *ngTemplateOutlet="
                bar;
                context: { $implicit: row.hub.light, label: 'Hub ' + row.hub.id + ' light' }
              "
            />
            <ul class="gondolas" [id]="'lights-hub-' + row.hub.id" [hidden]="!row.expanded">
              @if (row.expanded) {
                @for (gondola of row.gondolas; track gondola.id) {
                  <li class="row gondola-row">
                    <span class="name">Gondola {{ gondola.id }}</span>
                    <ng-container
                      *ngTemplateOutlet="
                        bar;
                        context: {
                          $implicit: gondola.light,
                          label: 'Gondola ' + gondola.id + ' light',
                        }
                      "
                    />
                  </li>
                }
              }
            </ul>
          </li>
        }
      </ul>
      <p class="total" role="status" aria-live="polite">
        Total output <span class="value">{{ total() }}</span>
      </p>
    </section>

    <ng-template #bar let-light let-label="label">
      <p-progressbar
        class="bar"
        [value]="percent(light)"
        [showValue]="false"
        [attr.aria-label]="label"
        [attr.aria-valuetext]="lumens(light)"
      />
      <span class="value" aria-hidden="true">{{ lumens(light) }}</span>
    </ng-template>
  `,
  styles: `
    .lights {
      display: grid;
      gap: 0.6rem;
    }
    h2 {
      margin: 0;
      font-size: 1rem;
    }
    .rows,
    .gondolas {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.35rem;
    }
    .row {
      display: grid;
      grid-template-columns: 6rem 1fr 5rem;
      align-items: center;
      gap: 0.5rem;
    }
    .gondolas {
      grid-column: 1 / -1;
      padding-left: 1rem;
    }
    .gondolas[hidden] {
      display: none;
    }
    .toggle {
      justify-content: flex-start;
      padding-inline: 0.25rem;
      color: var(--bb-text);
    }
    .chevron {
      width: 0.8rem;
    }
    .bar {
      --p-progressbar-height: 0.55rem;
    }
    .value {
      text-align: right;
      white-space: nowrap;
      font-variant-numeric: tabular-nums;
    }
    .total {
      display: flex;
      justify-content: space-between;
      margin: 0;
      padding-top: 0.4rem;
      border-top: 1px solid var(--bb-border);
      font-weight: 600;
    }
  `,
})
export class LightsPanel {
  readonly mill = input.required<Mill>();
  readonly hubs = input.required<readonly Hub[]>();
  readonly gondolas = input.required<readonly Gondola[]>();

  /** The ids of the hubs whose gondola rows are expanded. */
  private readonly expandedHubs = signal<ReadonlySet<number>>(new Set());

  /** Each hub with its four gondolas and whether its row is expanded. */
  protected readonly hubRows = computed(() => {
    const expanded = this.expandedHubs();
    return this.hubs().map((hub) => ({
      hub,
      expanded: expanded.has(hub.id),
      gondolas: this.gondolas().filter(
        (gondola) => Math.floor((gondola.id - 1) / GONDOLAS_PER_HUB) === hub.id - 1,
      ),
    }));
  });

  /** The output of all 41 fixtures together. */
  protected readonly total = computed(() =>
    formatLumens(
      totalLightLumens({ mill: this.mill(), hubs: this.hubs(), gondolas: this.gondolas() }),
    ),
  );

  /** Expands a collapsed hub row, or collapses an expanded one. */
  protected toggle(hubId: number): void {
    this.expandedHubs.update((current) => {
      const next = new Set(current);
      if (!next.delete(hubId)) {
        next.add(hubId);
      }
      return next;
    });
  }

  /** The fixture's output as a percentage of its rating, to one decimal. */
  protected percent(light: FixtureLight): number {
    return Math.round(light.level * 1000) / 10;
  }

  protected lumens(light: FixtureLight): string {
    return formatLumens(light.lumens);
  }
}
