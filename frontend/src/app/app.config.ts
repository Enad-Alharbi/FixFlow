import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { routes } from './app.routes';
import { LanguageService } from './core/i18n/language.service';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';
import { FIXFLOW_TRANSLOCO_CONFIG } from './core/i18n/transloco.config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withFetch()),
    provideTransloco({ config: FIXFLOW_TRANSLOCO_CONFIG, loader: TranslocoHttpLoader }),
    // Choose the language and load its strings before the first render, so keys never flash on screen.
    provideAppInitializer(() => inject(LanguageService).init()),
  ],
};
