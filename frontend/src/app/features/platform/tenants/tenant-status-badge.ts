import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { IconArchive, IconCircleCheck, IconPlayerPause } from '@tabler/icons-angular';
import { StateBadge, StateTone } from '../../../shared/ui/state-badge/state-badge';
import { TenantStatus } from './tenants.api';

const LOOK = {
  Active: { tone: 'ok', icon: IconCircleCheck },
  Suspended: { tone: 'warn', icon: IconPlayerPause },
  Archived: { tone: 'off', icon: IconArchive },
} as const satisfies Record<TenantStatus, { tone: StateTone; icon: unknown }>;

/** Tenant lifecycle status. */
@Component({
  selector: 'app-tenant-status-badge',
  imports: [TranslocoDirective, StateBadge],
  template: `
    <app-state-badge *transloco="let t" [tone]="look().tone" [icon]="look().icon">
      {{ t('platform.tenantStatus.' + status()) }}
    </app-state-badge>
  `,
})
export class TenantStatusBadge {
  readonly status = input.required<TenantStatus>();
  protected readonly look = computed(() => LOOK[this.status()]);
}
