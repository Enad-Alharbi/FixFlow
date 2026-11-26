import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { IconAlertTriangle, IconArrowLeft, TablerIconComponent } from '@tabler/icons-angular';
import { apiErrorCode, ApiErrors } from '../../../core/http/api-error';
import { serverFieldCodes } from '../../../core/forms/coded';
import { LocalDatePipe } from '../../../core/i18n/local-date.pipe';
import { Button } from '../../../shared/ui/button/button';
import { Confirm } from '../../../shared/ui/dialog/dialog';
import { Toasts } from '../../../shared/ui/toast/toast';
import { TenantSettingsFields, createTenantSettingsForm } from './tenant-settings-fields';
import { TenantStatusBadge } from './tenant-status-badge';
import { TENANTS_URL, TenantAction, TenantDetails, TenantStatus, TenantsApi } from './tenants.api';

interface StatusAction {
  action: TenantAction;
  /** For unarchive: the status the tenant returns to. */
  to?: TenantStatus;
  /** Disruptive actions are confirmed first; the dialog's confirm button carries the danger style. */
  confirm: boolean;
}

/** Which lifecycle actions each status offers — the same table the API enforces (TenantService.AllowedFrom). */
const ACTIONS: Record<TenantStatus, StatusAction[]> = {
  Active: [
    { action: 'suspend', confirm: true },
    { action: 'archive', confirm: true },
  ],
  Suspended: [
    { action: 'reactivate', confirm: false },
    { action: 'archive', confirm: true },
  ],
  Archived: [
    { action: 'unarchive', to: 'Active', confirm: false },
    { action: 'unarchive', to: 'Suspended', confirm: false },
  ],
};

/** One tenant: its facts, its settings (editable), and its lifecycle actions. Users join this page in step 4e. */
@Component({
  selector: 'app-tenant-detail',
  imports: [
    RouterLink,
    TranslocoDirective,
    TablerIconComponent,
    LocalDatePipe,
    Button,
    TenantSettingsFields,
    TenantStatusBadge,
  ],
  templateUrl: './tenant-detail.html',
  styleUrl: './tenant-detail.scss',
})
export class TenantDetail {
  private readonly api = inject(TenantsApi);
  private readonly confirm = inject(Confirm);
  private readonly toasts = inject(Toasts);
  private readonly transloco = inject(TranslocoService);
  protected readonly apiErrors = inject(ApiErrors);

  /** Route parameter :id. */
  readonly id = input.required<string>();

  protected readonly tenant = httpResource<TenantDetails>(() => `${TENANTS_URL}/${this.id()}`);
  protected readonly actions = computed(() =>
    this.tenant.hasValue() ? ACTIONS[this.tenant.value().status] : [],
  );

  protected readonly form = createTenantSettingsForm();
  protected readonly submitted = signal(false);
  protected readonly saving = signal(false);
  protected readonly busyAction = signal<StatusAction | null>(null);
  protected readonly error = signal<unknown>(null);
  protected readonly serverCodes = signal<Record<string, string>>({});
  protected readonly conflict = computed(
    () => apiErrorCode(this.error()) === 'concurrency.conflict',
  );
  protected readonly icons = { back: IconArrowLeft, alert: IconAlertTriangle };

  constructor() {
    // Whenever a fresh copy arrives (first load, after saving, after Reload), the form shows exactly that copy.
    effect(() => {
      if (!this.tenant.hasValue()) return;
      const t = this.tenant.value();
      this.form.reset({
        name: t.name,
        defaultLanguage: t.defaultLanguage,
        timeZone: t.timeZone,
        contactName: t.contactName ?? '',
        contactEmail: t.contactEmail ?? '',
        contactPhone: t.contactPhone ?? '',
      });
      this.submitted.set(false);
    });
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      if (!this.conflict()) this.error.set(null);
      this.serverCodes.set({});
    });
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    this.error.set(null);
    if (this.form.invalid || this.saving() || !this.tenant.hasValue()) return;

    this.saving.set(true);
    try {
      const saved = await this.api.update(
        this.id(),
        this.form.getRawValue(),
        this.tenant.value().rowVersion,
      );
      this.tenant.set(saved);
      this.toasts.show('platform.tenants.saved');
    } catch (error) {
      this.error.set(error);
      this.serverCodes.set(serverFieldCodes(error));
    } finally {
      this.saving.set(false);
    }
  }

  /** Someone else saved first (409): load their version; the admin then re-applies their change. */
  protected reload(): void {
    this.error.set(null);
    this.tenant.reload();
  }

  protected async run(item: StatusAction): Promise<void> {
    if (!this.tenant.hasValue() || this.busyAction()) return;
    const name = this.tenant.value().name;
    const key = this.actionKey(item);
    if (item.confirm) {
      const ok = await this.confirm.ask({
        title: this.transloco.translate(`platform.tenants.actions.${key}.title`, { name }),
        message: this.transloco.translate(`platform.tenants.actions.${key}.message`),
        confirm: this.transloco.translate(`platform.tenants.actions.${key}.button`),
        danger: true,
      });
      if (!ok) return;
    }

    this.busyAction.set(item);
    this.error.set(null);
    try {
      this.tenant.set(await this.api.changeStatus(this.id(), item.action, item.to));
      this.toasts.show(`platform.tenants.actions.${key}.done`, { name });
    } catch (error) {
      this.error.set(error);
    } finally {
      this.busyAction.set(null);
    }
  }

  /** Translation key for an action: "suspend", …, "unarchiveActive", "unarchiveSuspended". */
  protected actionKey(item: StatusAction): string {
    return item.to ? `${item.action}${item.to}` : item.action;
  }

  /** Field errors appear under their fields; this is the alert for everything else. */
  protected alertKey(): string | null {
    const error = this.error();
    if (!error) return null;
    if (error instanceof HttpErrorResponse && error.status === 400)
      return 'errors.validation.failed';
    return this.apiErrors.key(error);
  }
}
