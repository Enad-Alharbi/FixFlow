import { Dialog } from '@angular/cdk/dialog';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { IconAlertTriangle, IconArrowLeft, TablerIconComponent } from '@tabler/icons-angular';
import { firstValueFrom } from 'rxjs';
import { serverFieldCodes } from '../../../core/forms/coded';
import { ApiErrors } from '../../../core/http/api-error';
import { LocalDatePipe } from '../../../core/i18n/local-date.pipe';
import { Button } from '../../../shared/ui/button/button';
import { Confirm, dialogOptions } from '../../../shared/ui/dialog/dialog';
import { Field, FieldInput } from '../../../shared/ui/field/field';
import { Toasts } from '../../../shared/ui/toast/toast';
import {
  TENANT_ROLES,
  TenantRole,
  UserFields,
  createUserForm,
} from '../../users/user-fields/user-fields';
import { TENANTS_URL, TenantDetails } from '../tenants/tenants.api';
import { AccountStateBadge } from './account-state-badge';
import { SetPasswordData, SetPasswordDialog } from './user-dialogs';
import { UserDetails, UsersApi, accountState, userUrl } from './users.api';

type AccountAction = 'deactivate' | 'reactivate' | 'unlock' | 'delete' | 'restore';

/**
 * One tenant user in the Control Panel: profile, role, and account actions (CLAUDE.md §7.4, §10.1).
 * A deleted user can only be restored. The API enforces every rule (last Supervisor, D18) and this page shows
 * its answer.
 */
@Component({
  selector: 'app-user-detail',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoDirective,
    TablerIconComponent,
    LocalDatePipe,
    Button,
    Field,
    FieldInput,
    UserFields,
    AccountStateBadge,
  ],
  templateUrl: './user-detail.html',
  styleUrl: './user-detail.scss',
})
export class UserDetail {
  private readonly api = inject(UsersApi);
  private readonly confirm = inject(Confirm);
  private readonly dialog = inject(Dialog);
  private readonly toasts = inject(Toasts);
  private readonly transloco = inject(TranslocoService);
  protected readonly apiErrors = inject(ApiErrors);

  // Route parameters.
  readonly tenantId = input.required<string>();
  readonly id = input.required<string>();

  protected readonly user = httpResource<UserDetails>(() => userUrl(this.tenantId(), this.id()));
  protected readonly tenant = httpResource<TenantDetails>(
    () => `${TENANTS_URL}/${this.tenantId()}`,
  );
  protected readonly state = computed(() =>
    this.user.hasValue() ? accountState(this.user.value()) : null,
  );

  protected readonly form = createUserForm();
  protected readonly role = new FormControl<TenantRole>('Requester', { nonNullable: true });
  protected readonly roles = TENANT_ROLES;
  protected readonly submitted = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<unknown>(null);
  protected readonly serverCodes = signal<Record<string, string>>({});
  protected readonly icons = { back: IconArrowLeft, alert: IconAlertTriangle };

  /** The profile as stored; the form resets only when THIS changes, so an unlock doesn't wipe typed edits. */
  private readonly storedProfile = computed(() => {
    if (!this.user.hasValue()) return null;
    const u = this.user.value();
    return JSON.stringify([u.displayName, u.userName, u.email, u.phone, u.role, u.isDeleted]);
  });

  constructor() {
    // Role and password have their own actions, so the profile form leaves them out.
    this.form.controls.role.disable();
    this.form.controls.password.disable();

    effect(() => {
      if (!this.storedProfile()) return;
      const u = untracked(() => this.user.value()!);
      this.form.reset({
        displayName: u.displayName,
        userName: u.userName,
        email: u.email,
        phone: u.phone ?? '',
      });
      this.role.setValue(u.role);
      this.submitted.set(false);
      // A deleted user is read-only until restored.
      if (u.isDeleted) {
        this.form.disable();
        this.role.disable();
      } else {
        this.form.enable();
        this.form.controls.role.disable();
        this.form.controls.password.disable();
        this.role.enable();
      }
    });
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.serverCodes.set({}));
  }

  protected async saveProfile(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (this.form.invalid) return;
    const { displayName, userName, email, phone } = this.form.getRawValue();
    await this.run(
      'save',
      () => this.api.update(this.tenantId(), this.id(), { displayName, userName, email, phone }),
      'platform.users.saved',
    );
  }

  protected async changeRole(): Promise<void> {
    const role = this.role.value;
    const ok = await this.confirm.ask({
      title: this.transloco.translate('platform.users.roleConfirm.title', {
        role: this.transloco.translate(`roles.${role}`),
      }),
      message: this.transloco.translate('platform.users.roleConfirm.message'),
      confirm: this.transloco.translate('platform.users.changeRole'),
    });
    if (ok)
      await this.run(
        'role',
        () => this.api.changeRole(this.tenantId(), this.id(), role),
        'platform.users.roleChanged',
      );
  }

  protected async setPassword(): Promise<void> {
    const title = this.transloco.translate('platform.users.setPasswordFor', {
      name: this.user.value()?.displayName,
    });
    const ref = this.dialog.open<UserDetails, SetPasswordData>(
      SetPasswordDialog,
      dialogOptions(title, { tenantId: this.tenantId(), userId: this.id(), title }),
    );
    const updated = await firstValueFrom(ref.closed);
    if (!updated) return;
    this.user.set(updated);
    this.toasts.show('platform.users.passwordSet');
  }

  /** Deactivate and delete are confirmed first; the rest are easy to undo. */
  protected async act(action: AccountAction): Promise<void> {
    const name = this.user.value()?.displayName;
    if (action === 'deactivate' || action === 'delete') {
      const ok = await this.confirm.ask({
        title: this.transloco.translate(`platform.users.actions.${action}.title`, { name }),
        message: this.transloco.translate(`platform.users.actions.${action}.message`),
        confirm: this.transloco.translate(`platform.users.actions.${action}.button`),
        danger: true,
      });
      if (!ok) return;
    }
    const call =
      action === 'delete'
        ? () => this.api.delete(this.tenantId(), this.id())
        : () => this.api.act(this.tenantId(), this.id(), action);
    await this.run(action, call, `platform.users.actions.${action}.done`, { name });
  }

  /** One request at a time; on success show the API's copy of the user, on failure its error. */
  private async run(
    name: string,
    call: () => Promise<UserDetails>,
    doneKey: string,
    params?: Record<string, unknown>,
  ): Promise<void> {
    if (this.busy()) return;
    this.busy.set(name);
    this.error.set(null);
    try {
      this.user.set(await call());
      this.toasts.show(doneKey, params);
    } catch (error) {
      this.error.set(error);
      this.serverCodes.set(serverFieldCodes(error));
    } finally {
      this.busy.set(null);
    }
  }

  protected alertKey(): string | null {
    const error = this.error();
    if (!error) return null;
    if (error instanceof HttpErrorResponse && error.status === 400)
      return 'errors.validation.failed';
    return this.apiErrors.key(error);
  }
}
