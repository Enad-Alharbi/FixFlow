import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { provideTablerIconConfig } from '@tabler/icons-angular';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { LanguageService } from './core/i18n/language.service';
import { TranslatedTitleStrategy } from './core/i18n/translated-title-strategy';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';
import { FIXFLOW_TRANSLOCO_CONFIG } from './core/i18n/transloco.config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // withComponentInputBinding: route data (the shell's `plane`) and query params (`returnUrl`) arrive as inputs.
    provideRouter(routes, withComponentInputBinding()),
    { provide: TitleStrategy, useClass: TranslatedTitleStrategy },
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideTransloco({ config: FIXFLOW_TRANSLOCO_CONFIG, loader: TranslocoHttpLoader }),
    // 1.75 stroke matches Barlow's weight (docs/design/design-direction.md §6). Size comes from CSS (1.25em).
    provideTablerIconConfig({ stroke: 1.75 }),
    // Before the first render: restore the session from the refresh cookie (so guards know who is signed in),
    // then pick the language (it may come from the tenant) and load its strings, so keys never flash on screen.
    provideAppInitializer(async () => {
      const auth = inject(AuthService);
      const language = inject(LanguageService);
      await auth.restore();
      await language.init(auth.user()?.tenant?.defaultLanguage);
    }),
  ],
};
