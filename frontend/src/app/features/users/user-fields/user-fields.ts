import { Component, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  IconCopy,
  IconEye,
  IconEyeOff,
  IconWand,
  TablerIconComponent,
} from '@tabler/icons-angular';
import { isValidEmail, isValidPassword, isValidUsername } from '../../../core/auth/account-rules';
import { UserRole } from '../../../core/auth/auth.service';
import { generatePassword } from '../../../core/auth/password-generator';
import { coded, shownCode } from '../../../core/forms/coded';
import { ApiErrors } from '../../../core/http/api-error';
import { Button } from '../../../shared/ui/button/button';
import { Field, FieldInput } from '../../../shared/ui/field/field';
import { PasswordChecklist } from '../../../shared/ui/password-checklist/password-checklist';
import { Toasts } from '../../../shared/ui/toast/toast';

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
 * in Phase 5, for Supervisors. The parent owns the form and the submit; this renders fields, the password
 * generator, and the live checklist.
 */
@Component({
  selector: 'app-user-fields',
  imports: [
    ReactiveFormsModule,
    TranslocoDirective,
    TablerIconComponent,
    Button,
    Field,
    FieldInput,
    PasswordChecklist,
  ],
  templateUrl: './user-fields.html',
  styles: `
    .fields {
      display: grid;
      gap: var(--space-5);
    }

    .password-tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
})
export class UserFields {
  private readonly toasts = inject(Toasts);
  private readonly apiErrors = inject(ApiErrors);

  readonly form = input.required<UserForm>();
  /** Server codes per field (see serverFieldCodes). */
  readonly serverCodes = input<Record<string, string>>({});
  readonly submitted = input(false);
  /** The first Supervisor is always a Supervisor, so the tenant flow hides the role. */
  readonly showRole = input(true);

  protected readonly roles = TENANT_ROLES;
  protected readonly showPassword = signal(false);
  protected readonly icons = {
    show: IconEye,
    hide: IconEyeOff,
    generate: IconWand,
    copy: IconCopy,
  };

  /** Translated error under a field, or null. `t` is the template's translate function. */
  protected error(field: UserField, t: (key: string) => string): string | null {
    const code = shownCode(
      this.form().controls[field],
      this.submitted(),
      this.serverCodes()[field],
    );
    return code ? t(this.apiErrors.codeKey(code)) : null;
  }

  /** Fills in a generated password and shows it, so the creator can read or copy it for the new user. */
  protected generate(): void {
    this.form().controls.password.setValue(generatePassword());
    this.form().controls.password.markAsTouched();
    this.showPassword.set(true);
  }

  protected async copy(): Promise<void> {
    await navigator.clipboard.writeText(this.form().controls.password.value);
    this.toasts.show('users.passwordCopied');
  }
}
