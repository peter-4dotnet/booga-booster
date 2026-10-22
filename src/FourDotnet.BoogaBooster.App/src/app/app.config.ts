import { provideHttpClient, withFetch } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import Aura from '@primeuix/themes/aura';
import { providePrimeNG } from 'primeng/config';

import { routes } from './app.routes';
import { HttpQueueSource } from './queue/data/http-queue-source';
import { QUEUE_SOURCE } from './queue/data/queue-source';
import { RIDE_TELEMETRY_SOURCE } from './ride-dashboard/data/ride-telemetry-source';
import { SseRideTelemetrySource } from './ride-dashboard/data/sse-ride-telemetry-source';
import { HttpWeatherSource } from './weather/data/http-weather-source';
import { WEATHER_SOURCE } from './weather/data/weather-source';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withFetch()),
    // PrimeNG's Aura theme. The dashboard is always dark, so the dark palette is
    // bound to the `bb-dark` class set on <html> in index.html.
    providePrimeNG({ theme: { preset: Aura, options: { darkModeSelector: '.bb-dark' } } }),
    { provide: RIDE_TELEMETRY_SOURCE, useExisting: SseRideTelemetrySource },
    { provide: WEATHER_SOURCE, useExisting: HttpWeatherSource },
    { provide: QUEUE_SOURCE, useExisting: HttpQueueSource },
  ],
};
