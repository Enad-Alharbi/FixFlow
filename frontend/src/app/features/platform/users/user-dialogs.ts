import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { isValidPassword } from '../../../core/auth/account-rules';
import { coded, serverFieldCodes, shownCode } from '../../../core/forms/coded';
import { ApiErrors } from '../../../core/http/api-error';
import { Button } from '../../../shared/ui/button/button';
import { DialogFrame } from '../../../shared/ui/dialog/dialog';
import { PasswordField } from '../../users/password-field/password-field';
import { UserFields, createUserForm } from '../../users/user-fields/user-fields';
import { UserDetails, UsersApi } from './users.api';

/** Field errors appear under their fields; anything else (409, network) as one message. */
function alertKey(apiErrors: ApiErrors, error: unknown): string | null {
  if (!error) return null;
  return error instanceof HttpErrorResponse && error.status === 400
    ? 'errors.validation.failed'
    : apiErrors.key(error);
}

export interface CreateUserData {
  tenantId: string;
  title: string;
}

/** The shared Create User modal (CLAUDE.md §7.4). Closes with the created user, or nothing when cancelled. */
@Component({
  selector: 'app-create-user-dialog',
  imports: [TranslocoDirective, DialogFrame, Button, UserFields],
  template: `
    <form (submit)="submit($event)" novalidate *transloco="let t">
      <app-dialog [title]="data.title">
        <app-user-fields [form]="form" [serverCodes]="serverCodes()" [submitted]="submitted()" />
        <div role="alert">
          @if (alert(); as key) {
            <p class="alert">{{ t(key) }}</p>
          }
        </div>
        <div dialogActions>
          <button appButton type="button" (click)="ref.close()">{{ t('common.cancel') }}</button>
          <button appButton variant="primary" type="submit" [loading]="saving()">
            {{ t('platform.users.create') }}
          </button>
        </div>
      </app-dialog>
    </form>
  `,
})
export class CreateUserDialog {
  private readonly api = inject(UsersApi);
  private readonly apiErrors = inject(ApiErrors);
  protected readonly data = inject<CreateUserData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<UserDetails>>(DialogRef);

  protected readonly form = createUserForm();
  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly serverCodes = signal<Record<string, string>>({});
  protected readonly alert = signal<string | null>(null);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    this.serverCodes.set({});
    this.alert.set(null);
    if (this.form.invalid || this.saving()) return;

    this.saving.set(true);
    try {
      this.ref.close(await this.api.create(this.data.tenantId, this.form.getRawValue()));
    } catch (error) {
      this.serverCodes.set(serverFieldCodes(error));
      this.alert.set(alertKey(this.apiErrors, error));
    } finally {
      this.saving.set(false);
    }
  }
}

export interface SetPasswordData {
  tenantId: string;
  userId: string;
  title: string;
}

/** An admin sets a new password (no current password needed); the user's sessions end. */
@Component({
  selector: 'app-set-password-dialog',
  imports: [TranslocoDirective, DialogFrame, Button, PasswordField],
  template: `
    <form (submit)="submit($event)" novalidate *transloco="let t">
      <app-dialog [title]="data.title">
        <p class="note">{{ t('platform.users.setPasswordNote') }}</p>
        <app-password-field [control]="password" [error]="error() ? t(error()!) : null" />
        <div role="alert">
          @if (alert(); as key) {
            <p class="alert">{{ t(key) }}</p>
          }
        </div>
        <div dialogActions>
          <button appButton type="button" (click)="ref.close()">{{ t('common.cancel') }}</button>
          <button appButton variant="primary" type="submit" [loading]="saving()">
            {{ t('platform.users.setPassword') }}
          </button>
        </div>
      </app-dialog>
    </form>
  `,
  styles: `
    .note {
      margin: 0;
      color: var(--color-text-muted);
    }
  `,
})
export class SetPasswordDialog {
  private readonly api = inject(UsersApi);
  private readonly apiErrors = inject(ApiErrors);
  protected readonly data = inject<SetPasswordData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<UserDetails>>(DialogRef);

  protected readonly password = new FormControl('', {
    nonNullable: true,
    validators: [coded('user.password_too_weak', isValidPassword)],
  });
  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  private readonly serverCode = signal<string | undefined>(undefined);
  protected readonly alert = signal<string | null>(null);

  /** Translation key of the password error, or null. */
  protected error(): string | null {
    const code = shownCode(this.password, this.submitted(), this.serverCode());
    return code ? this.apiErrors.codeKey(code) : null;
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    this.serverCode.set(undefined);
    this.alert.set(null);
    if (this.password.invalid || this.saving()) return;

    this.saving.set(true);
    try {
      const { tenantId, userId } = this.data;
      this.ref.close(await this.api.setPassword(tenantId, userId, this.password.value));
    } catch (error) {
      this.serverCode.set(serverFieldCodes(error)['password']);
      this.alert.set(alertKey(this.apiErrors, error));
    } finally {
      this.saving.set(false);
    }
  }
}
