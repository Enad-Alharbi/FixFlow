import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { provideTablerIconConfig } from '@tabler/icons-angular';
import { routes } from './app.routes';
import { LanguageService } from './core/i18n/language.service';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';
import { FIXFLOW_TRANSLOCO_CONFIG } from './core/i18n/transloco.config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // withComponentInputBinding: route data (e.g. the shell's `plane`) arrives as component inputs.
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch()),
    provideTransloco({ config: FIXFLOW_TRANSLOCO_CONFIG, loader: TranslocoHttpLoader }),
    // 1.75 stroke matches Barlow's weight (docs/design/design-direction.md §6). Size comes from CSS (1.25em).
    provideTablerIconConfig({ stroke: 1.75 }),
    // Choose the language and load its strings before the first render, so keys never flash on screen.
    provideAppInitializer(() => inject(LanguageService).init()),
  ],
};
