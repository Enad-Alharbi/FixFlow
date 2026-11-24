import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from './language.service';

/**
 * Formats a date for the current language with Intl (Arabic always with Western digits).
 * Impure so it re-formats when the language changes; formatters are cached per locale and style.
 *   {{ checkedAt | localDate: 'time' }}
 */
@Pipe({ name: 'localDate', pure: false })
export class LocalDatePipe implements PipeTransform {
  private readonly language = inject(LanguageService);
  private static readonly cache = new Map<string, Intl.DateTimeFormat>();

  // shortcut: formats in the browser's time zone; switch to the tenant's time zone once tenants exist (Phase 3).
  transform(
    value: Date | string | null | undefined,
    style: 'datetime' | 'date' | 'time' = 'datetime',
  ): string {
    if (value == null) return '';
    const locale = this.language.locale();
    const key = `${locale}|${style}`;
    let format = LocalDatePipe.cache.get(key);
    if (!format) {
      const options: Intl.DateTimeFormatOptions =
        style === 'date'
          ? { dateStyle: 'medium' }
          : style === 'time'
            ? { timeStyle: 'short' }
            : { dateStyle: 'medium', timeStyle: 'short' };
      format = new Intl.DateTimeFormat(locale, options);
      LocalDatePipe.cache.set(key, format);
    }
    return format.format(new Date(value));
  }
}
