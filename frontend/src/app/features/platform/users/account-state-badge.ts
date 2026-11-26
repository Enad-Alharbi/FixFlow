import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { IconCircleCheck, IconCircleOff, IconLock, IconTrash } from '@tabler/icons-angular';
import { LocalDatePipe } from '../../../core/i18n/local-date.pipe';
import { StateBadge, StateTone } from '../../../shared/ui/state-badge/state-badge';
import { AccountState, UserDetails, accountState } from './users.api';

const LOOK = {
  active: { tone: 'ok', icon: IconCircleCheck },
  locked: { tone: 'warn', icon: IconLock },
  deactivated: { tone: 'off', icon: IconCircleOff },
  deleted: { tone: 'danger', icon: IconTrash },
} as const satisfies Record<AccountState, { tone: StateTone; icon: unknown }>;

/** An account's state; a locked account also shows when the lock ends (CLAUDE.md §7.4). */
@Component({
  selector: 'app-account-state-badge',
  imports: [TranslocoDirective, StateBadge, LocalDatePipe],
  template: `
    <app-state-badge *transloco="let t" [tone]="look().tone" [icon]="look().icon">
      @if (state() === 'locked') {
        {{
          t('platform.accountState.lockedUntil', { time: user().lockoutEnd | localDate: 'time' })
        }}
      } @else {
        {{ t('platform.accountState.' + state()) }}
      }
    </app-state-badge>
  `,
})
export class AccountStateBadge {
  readonly user = input.required<Pick<UserDetails, 'isDeleted' | 'isActive' | 'lockoutEnd'>>();
  protected readonly state = computed(() => accountState(this.user()));
  protected readonly look = computed(() => LOOK[this.state()]);
}
