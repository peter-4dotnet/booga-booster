import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { OperationControls } from './controls/operation-controls';
import { GondolaPanel } from './panels/gondola-panel/gondola-panel';
import { LightsPanel } from './panels/lights-panel/lights-panel';
import { SecurityPanel } from './panels/security-panel/security-panel';
import { SpeedPanel } from './panels/speed-panel/speed-panel';
import { StatusSummary } from './panels/status-summary/status-summary';
import { RideLifecycleService } from './state/ride-lifecycle.service';
import { RideStateService } from './state/ride-state.service';
import { RideVisualization, RigLightFrame } from './visualization/ride-visualization';
import { QueuePanel } from '../queue/queue-panel/queue-panel';
import { WeatherPanel } from '../weather/weather-panel/weather-panel';

/**
 * Landing-page shell for the ride operator. A three-column dashboard —
 * controls rail, central visualization, telemetry rail — that projects the
 * ride-state service's signals into small, single-responsibility panels.
 */
@Component({
  selector: 'bb-ride-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    StatusSummary,
    OperationControls,
    RideVisualization,
    SecurityPanel,
    SpeedPanel,
    GondolaPanel,
    QueuePanel,
    WeatherPanel,
    LightsPanel,
  ],
  templateUrl: './ride-dashboard.html',
  styleUrl: './ride-dashboard.scss',
})
export class RideDashboard {
  protected readonly ride = inject(RideStateService);
  protected readonly lifecycle = inject(RideLifecycleService);

  /** The light rig's latest output, for the visualization. */
  protected readonly lights = computed<RigLightFrame>(() => ({
    mill: this.ride.mill(),
    hubs: this.ride.hubs(),
    gondolas: this.ride.gondolas(),
  }));
}
