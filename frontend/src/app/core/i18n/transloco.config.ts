import { isDevMode } from '@angular/core';
import { translocoConfig } from '@jsverse/transloco';

/** Shared by the app (app.config.ts) and the tests (testing.ts), so tests run the real configuration. */
export const FIXFLOW_TRANSLOCO_CONFIG = translocoConfig({
  availableLangs: ['en', 'ar'],
  defaultLang: 'en',
  fallbackLang: 'en',
  reRenderOnLangChange: true,
  prodMode: !isDevMode(),
  missingHandler: { logMissingKey: true },
});
