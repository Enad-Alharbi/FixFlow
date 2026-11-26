import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { merge } from 'rxjs';
import { IconArrowLeft, IconAlertTriangle, TablerIconComponent } from '@tabler/icons-angular';
import { coded, serverFieldCodes, shownCode } from '../../../core/forms/coded';
import { ApiErrors } from '../../../core/http/api-error';
import { Button } from '../../../shared/ui/button/button';
import { Field, FieldInput } from '../../../shared/ui/field/field';
import { Toasts } from '../../../shared/ui/toast/toast';
import { UserFields, createUserForm } from '../../users/user-fields/user-fields';
import { TenantSettingsFields, createTenantSettingsForm } from './tenant-settings-fields';
import { TenantsApi } from './tenants.api';

/**
 * New tenant + its first Supervisor in one step (CLAUDE.md §10.1). The API creates both or neither, so a
 * rejected Supervisor (e.g. email taken) leaves no half-made tenant behind.
 */
@Component({
  selector: 'app-tenant-create',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoDirective,
    TablerIconComponent,
    Button,
    Field,
    FieldInput,
    TenantSettingsFields,
    UserFields,
  ],
  templateUrl: './tenant-create.html',
  styleUrl: './tenant-create.scss',
})
export class TenantCreate {
  private readonly api = inject(TenantsApi);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  protected readonly apiErrors = inject(ApiErrors);

  // Fixed after creation (D19): 2–30 characters, a–z, 0–9 and "-", starting with a letter.
  protected readonly code = new FormControl('', {
    nonNullable: true,
    validators: [coded('tenant.code_invalid', (v) => /^[a-z][a-z0-9-]{1,29}$/.test(v.trim()))],
  });
  protected readonly settings = createTenantSettingsForm();
  protected readonly supervisor = createUserForm('Supervisor');

  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<unknown>(null);
  protected readonly tenantCodes = signal<Record<string, string>>({});
  protected readonly supervisorCodes = signal<Record<string, string>>({});
  protected readonly icons = { back: IconArrowLeft, alert: IconAlertTriangle };

  constructor() {
    // A server error belongs to the value that caused it: editing any field clears them.
    merge(this.code.valueChanges, this.settings.valueChanges, this.supervisor.valueChanges)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.clearServerErrors());
  }

  protected codeError(t: (key: string) => string): string | null {
    const code = shownCode(this.code, this.submitted(), this.tenantCodes()['code']);
    return code ? t(this.apiErrors.codeKey(code)) : null;
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault(); // stay on the page: the API call does the submitting
    this.submitted.set(true);
    this.clearServerErrors();
    if (this.code.invalid || this.settings.invalid || this.supervisor.invalid || this.saving())
      return;

    this.saving.set(true);
    try {
      const tenant = await this.api.create({
        ...this.settings.getRawValue(),
        code: this.code.value.trim(),
        firstSupervisor: { ...this.supervisor.getRawValue(), role: 'Supervisor' },
      });
      this.toasts.show('platform.tenants.created', { name: tenant.name });
      await this.router.navigate(['/platform/tenants', tenant.id]);
    } catch (error) {
      this.error.set(error);
      this.tenantCodes.set(serverFieldCodes(error));
      this.supervisorCodes.set(serverFieldCodes(error, 'firstSupervisor'));
    } finally {
      this.saving.set(false);
    }
  }

  /** Field errors show under their fields; anything else (network, server) goes in the alert. */
  protected alertKey(): string | null {
    const error = this.error();
    if (!error) return null;
    const isValidation = error instanceof HttpErrorResponse && error.status === 400;
    return isValidation ? 'errors.validation.failed' : this.apiErrors.key(error);
  }

  private clearServerErrors(): void {
    this.error.set(null);
    this.tenantCodes.set({});
    this.supervisorCodes.set({});
  }
}
