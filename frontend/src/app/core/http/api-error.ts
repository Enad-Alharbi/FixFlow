import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

/**
 * Turns an API error into a translation key. The API sends a stable `code` (e.g. "auth.account_locked");
 * its message lives under `errors.<code>` in public/i18n. Unknown codes fall back to a generic message.
 */
@Injectable({ providedIn: 'root' })
export class ApiErrors {
  private readonly transloco = inject(TranslocoService);

  key(error: unknown): string {
    return this.codeKey(apiErrorCode(error), 'errors.unknown');
  }

  /** The translation key for one error code, e.g. a field's "user.email_taken"; unknown codes get the fallback. */
  codeKey(code: string, fallback = 'errors.validation.failed'): string {
    const key = `errors.${code}`;
    // getTranslation(lang) is that language's flattened key map ("errors.auth.account_locked": "…").
    // (Without an argument it returns a Map of every language.) Both languages have the same keys (i18n check).
    const keys = this.transloco.getTranslation(this.transloco.getActiveLang());
    return key in keys ? key : fallback;
  }
}

export function apiErrorCode(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'unknown';
  if (error.status === 0) return 'network'; // the API could not be reached at all
  const code: unknown = (error.error as { code?: unknown } | null)?.code;
  if (typeof code === 'string') return code;
  return error.status >= 500 ? 'server.error' : 'unknown';
}
