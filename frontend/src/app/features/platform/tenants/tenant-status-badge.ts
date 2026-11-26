import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  IconArchive,
  IconCircleCheck,
  IconPlayerPause,
  TablerIconComponent,
} from '@tabler/icons-angular';
import { TenantStatus } from './tenants.api';

const ICONS = { Active: IconCircleCheck, Suspended: IconPlayerPause, Archived: IconArchive };

/** Tenant lifecycle status: colour + icon + label, never colour alone. */
@Component({
  selector: 'app-tenant-status-badge',
  imports: [TranslocoDirective, TablerIconComponent],
  template: `
    <ng-container *transloco="let t">
      <tabler-icon [icon]="icon()" aria-hidden="true" />{{ t('platform.tenantStatus.' + status()) }}
    </ng-container>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      padding: var(--space-1) var(--space-2);
      background: var(--badge-bg);
      color: var(--badge-fg);
      font-family: var(--font-display);
      font-size: var(--fs-sm);
      font-weight: var(--fw-bold);
      white-space: nowrap;
    }

    :host([data-status='Active']) {
      --badge-bg: var(--tenant-active-bg);
      --badge-fg: var(--tenant-active-fg);
    }

    :host([data-status='Suspended']) {
      --badge-bg: var(--tenant-suspended-bg);
      --badge-fg: var(--tenant-suspended-fg);
    }

    :host([data-status='Archived']) {
      --badge-bg: var(--tenant-archived-bg);
      --badge-fg: var(--tenant-archived-fg);
    }
  `,
  host: { '[attr.data-status]': 'status()' },
})
export class TenantStatusBadge {
  readonly status = input.required<TenantStatus>();
  protected readonly icon = computed(() => ICONS[this.status()]);
}
