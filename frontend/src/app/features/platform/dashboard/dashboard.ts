import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { environment } from '../../../../environments/environment';
import { ApiErrors } from '../../../core/http/api-error';
import { LocalDatePipe } from '../../../core/i18n/local-date.pipe';
import { Button } from '../../../shared/ui/button/button';
import { StateTone } from '../../../shared/ui/state-badge/state-badge';

/** Mirrors Features/Platform/PlatformDashboardService.cs. */
interface PlatformDashboard {
  tenants: { active: number; suspended: number; archived: number };
  users: { active: number; deactivated: number; locked: number; deleted: number };
  recentActivity: {
    id: string;
    at: string;
    action: string;
    actorName: string | null;
    tenantId: string | null;
    tenantName: string | null;
    targetType: 'Tenant' | 'User';
    targetId: string | null;
    targetName: string | null;
  }[];
}

interface Segment {
  key: string;
  count: number;
  tone: StateTone;
  link: { path: string; query?: Record<string, string> };
}

/** Every audit action the API writes today; anything newer falls back to its raw code. */
const KNOWN_ACTIONS = new Set([
  'tenant.created',
  'tenant.updated',
  'tenant.suspended',
  'tenant.reactivated',
  'tenant.archived',
  'tenant.unarchived',
  'user.created',
  'user.updated',
  'user.role_changed',
  'user.deactivated',
  'user.reactivated',
  'user.password_set',
  'user.unlocked',
  'user.deleted',
  'user.restored',
]);

/**
 * The basic platform dashboard (Phase 4): where tenants and accounts stand, and what the admins did lately.
 * Real counts only. Each group is a strip split by state (bar + legend with numbers), not a row of stat cards.
 */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, TranslocoDirective, LocalDatePipe, Button],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  protected readonly apiErrors = inject(ApiErrors);
  protected readonly data = httpResource<PlatformDashboard>(
    () => `${environment.apiUrl}/api/platform/dashboard`,
  );

  protected readonly tenantSegments = computed<Segment[]>(() => {
    const t = this.data.value()?.tenants;
    if (!t) return [];
    return [
      {
        key: 'Active',
        count: t.active,
        tone: 'ok',
        link: { path: '/platform/tenants', query: { status: 'Active' } },
      },
      {
        key: 'Suspended',
        count: t.suspended,
        tone: 'warn',
        link: { path: '/platform/tenants', query: { status: 'Suspended' } },
      },
      {
        key: 'Archived',
        count: t.archived,
        tone: 'off',
        link: { path: '/platform/tenants', query: { status: 'Archived' } },
      },
    ];
  });

  protected readonly userSegments = computed<Segment[]>(() => {
    const u = this.data.value()?.users;
    if (!u) return [];
    return [
      { key: 'active', count: u.active, tone: 'ok', link: { path: '/platform/users' } },
      { key: 'locked', count: u.locked, tone: 'warn', link: { path: '/platform/users' } },
      { key: 'deactivated', count: u.deactivated, tone: 'off', link: { path: '/platform/users' } },
      {
        key: 'deleted',
        count: u.deleted,
        tone: 'danger',
        link: { path: '/platform/users/deleted' },
      },
    ];
  });

  protected total(segments: Segment[]): number {
    return segments.reduce((sum, s) => sum + s.count, 0);
  }

  /** "tenant.suspended" -> its translation key; unknown actions show their code. */
  protected actionKey(action: string): string | null {
    return KNOWN_ACTIONS.has(action) ? `platform.activity.${action.replace('.', '_')}` : null;
  }
}
