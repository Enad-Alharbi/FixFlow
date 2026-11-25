import { Pipe, PipeTransform, inject } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { LanguageService } from './language.service';

/**
 * Formats a date for the current language with Intl (Arabic always with Western digits), in the signed-in
 * user's TENANT time zone (CLAUDE.md §16: stored in UTC, shown in the tenant's zone). Signed out or Platform
 * Admin: the browser's zone. Impure so it re-formats when the language changes; formatters are cached.
 *   {{ checkedAt | localDate: 'time' }}
 */
@Pipe({ name: 'localDate', pure: false })
export class LocalDatePipe implements PipeTransform {
  private readonly language = inject(LanguageService);
  private readonly auth = inject(AuthService);
  private static readonly cache = new Map<string, Intl.DateTimeFormat>();

  transform(
    value: Date | string | null | undefined,
    style: 'datetime' | 'date' | 'time' = 'datetime',
  ): string {
    if (value == null) return '';
    const locale = this.language.locale();
    const timeZone = this.auth.user()?.tenant?.timeZone; // undefined = browser's zone
    const key = `${locale}|${style}|${timeZone ?? ''}`;
    let format = LocalDatePipe.cache.get(key);
    if (!format) {
      const options: Intl.DateTimeFormatOptions =
        style === 'date'
          ? { dateStyle: 'medium' }
          : style === 'time'
            ? { timeStyle: 'short' }
            : { dateStyle: 'medium', timeStyle: 'short' };
      format = new Intl.DateTimeFormat(locale, { ...options, timeZone });
      LocalDatePipe.cache.set(key, format);
    }
    return format.format(new Date(value));
  }
}
