import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, ValidatorFn } from '@angular/forms';

/**
 * Client-side validation that speaks the API's language: a failed check stores the SAME error code the server
 * would return (e.g. "user.email_invalid"), so one translation (errors.<code>) serves both.
 *   new FormControl('', { validators: [coded('user.email_invalid', (v) => isValidEmail(v.trim()))] })
 * The value is passed as typed (passwords must not be trimmed); trim inside `ok` where the server trims.
 */
export function coded(code: string, ok: (value: string) => boolean): ValidatorFn {
  return (control) => (ok((control.value as string | null) ?? '') ? null : { code });
}

/** The code to show under a field: the client's own check once touched/submitted, otherwise the server's. */
export function shownCode(
  control: AbstractControl,
  submitted: boolean,
  serverCode: string | undefined,
): string | null {
  const own = control.errors?.['code'] as string | undefined;
  if (own && (control.touched || submitted)) return own;
  return serverCode ?? null;
}

/**
 * Per-field codes from a 400 validation.failed response, first code per field:
 *   { "errors": { "email": ["user.email_taken"] } } -> { email: "user.email_taken" }
 * A prefix picks a nested object's fields: prefix "firstSupervisor" turns "firstSupervisor.email" into "email".
 */
export function serverFieldCodes(error: unknown, prefix?: string): Record<string, string> {
  if (!(error instanceof HttpErrorResponse)) return {};
  const errors = (error.error as { errors?: Record<string, string[]> } | null)?.errors ?? {};
  const out: Record<string, string> = {};
  for (const [field, codes] of Object.entries(errors)) {
    const name = prefix
      ? field.startsWith(`${prefix}.`)
        ? field.slice(prefix.length + 1)
        : null
      : field;
    if (name && codes[0]) out[name] = codes[0];
  }
  return out;
}
