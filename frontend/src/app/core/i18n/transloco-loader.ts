import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Translation, TranslocoLoader } from '@jsverse/transloco';

/**
 * Loads translation files from public/i18n (served at /i18n).
 * Root strings: /i18n/en.json. A feature scope asks for "system-status/en" -> /i18n/system-status/en.json.
 */
@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(path: string) {
    return this.http.get<Translation>(`/i18n/${path}.json`);
  }
}
