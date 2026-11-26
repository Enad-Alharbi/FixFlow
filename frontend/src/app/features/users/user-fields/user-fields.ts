import { Component, inject, input } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { isValidEmail, isValidPassword, isValidUsername } from '../../../core/auth/account-rules';
import { UserRole } from '../../../core/auth/auth.service';
import { coded, shownCode } from '../../../core/forms/coded';
import { ApiErrors } from '../../../core/http/api-error';
import { Field, FieldInput } from '../../../shared/ui/field/field';
import { PasswordField } from '../password-field/password-field';

export type TenantRole = Exclude<UserRole, 'PlatformAdmin'>;
export const TENANT_ROLES: TenantRole[] = ['Requester', 'Technician', 'Supervisor'];

/** The Create User form (CLAUDE.md §7.4), with the same rules and error codes as the API (Users/UserRules.cs). */
export function createUserForm(role: TenantRole = 'Requester') {
  const text = (code: string, ok: (v: string) => boolean) =>
    new FormControl('', { nonNullable: true, validators: [coded(code, ok)] });
  return new FormGroup({
    displayName: text(
      'user.display_name_invalid',
      (v) => v.trim().length >= 2 && v.trim().length <= 100,
    ),
    userName: text('user.username_invalid', (v) => isValidUsername(v.trim())),
    email: text('user.email_invalid', (v) => isValidEmail(v.trim())),
    phone: new FormControl('', { nonNullable: true }), // optional; the server checks the format
    role: new FormControl<TenantRole>(role, { nonNullable: true }),
    password: text('user.password_too_weak', isValidPassword),
  });
}

export type UserForm = ReturnType<typeof createUserForm>;
type UserField = keyof UserForm['controls'];

/**
 * The fields of the shared Create User form, for the Control Panel (first Supervisor, any tenant's users) and,
 * in Phase 5, for Supervisors. The parent owns the form and the submit; this renders the fields. Editing an
 * existing user hides role and password (they have their own actions) and disables those controls.
 */
@Component({
  selector: 'app-user-fields',
  imports: [ReactiveFormsModule, TranslocoDirective, Field, FieldInput, PasswordField],
  templateUrl: './user-fields.html',
  styles: `
    .fields {
      display: grid;
      gap: var(--space-5);
    }
  `,
})
export class UserFields {
  private readonly apiErrors = inject(ApiErrors);

  readonly form = input.required<UserForm>();
  /** Server codes per field (see serverFieldCodes). */
  readonly serverCodes = input<Record<string, string>>({});
  readonly submitted = input(false);
  /** The first Supervisor is always a Supervisor, so the tenant flow hides the role. */
  readonly showRole = input(true);
  /** Off when editing an existing user: a password is set through its own action. */
  readonly withPassword = input(true);

  protected readonly roles = TENANT_ROLES;

  /** Translated error under a field, or null. `t` is the template's translate function. */
  protected error(field: UserField, t: (key: string) => string): string | null {
    const code = shownCode(
      this.form().controls[field],
      this.submitted(),
      this.serverCodes()[field],
    );
    return code ? t(this.apiErrors.codeKey(code)) : null;
  }
}
