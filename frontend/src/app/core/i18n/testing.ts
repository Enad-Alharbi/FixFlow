import { provideTranslocoScope, TranslocoTestingModule } from '@jsverse/transloco';
import en from '../../../../public/i18n/en.json';
import ar from '../../../../public/i18n/ar.json';
import authEn from '../../../../public/i18n/auth/en.json';
import authAr from '../../../../public/i18n/auth/ar.json';
import systemStatusEn from '../../../../public/i18n/system-status/en.json';
import systemStatusAr from '../../../../public/i18n/system-status/ar.json';
import { FIXFLOW_TRANSLOCO_CONFIG } from './transloco.config';

/** Test setup with the real translation files, so tests break when a string or key changes. */
export function provideTestTranslations() {
  return [
    TranslocoTestingModule.forRoot({
      langs: {
        en,
        ar,
        'auth/en': authEn,
        'auth/ar': authAr,
        'system-status/en': systemStatusEn,
        'system-status/ar': systemStatusAr,
      },
      translocoConfig: FIXFLOW_TRANSLOCO_CONFIG,
      preloadLangs: true,
    }).providers ?? [],
    provideTranslocoScope('system-status'),
    provideTranslocoScope('auth'),
  ];
}
