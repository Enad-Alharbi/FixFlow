import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { IconCircle, IconCircleCheck, TablerIconComponent } from '@tabler/icons-angular';
import {
  PASSWORD_MAX,
  PASSWORD_MIN,
  PASSWORD_RULES,
  checkPassword,
} from '../../../core/auth/account-rules';

/**
 * Live password checklist (CLAUDE.md §7.4): each rule ticks as the user types. Icon + text + a hidden
 * "met / not met" word, so the state never depends on colour alone. Used by Create User and Change Password.
 */
@Component({
  selector: 'app-password-checklist',
  imports: [TranslocoDirective, TablerIconComponent],
  template: `
    <ul *transloco="let t; prefix: 'passwordRules'">
      @for (rule of rules; track rule) {
        <li [class.met]="checks()[rule]">
          <tabler-icon [icon]="checks()[rule] ? metIcon : unmetIcon" aria-hidden="true" />
          <span>{{ t(rule, { min, max }) }}</span>
          <span class="visually-hidden">{{ checks()[rule] ? t('met') : t('notMet') }}</span>
        </li>
      }
    </ul>
  `,
  styles: `
    ul {
      display: grid;
      gap: var(--space-1);
      margin: 0;
      padding: 0;
      list-style: none;
      font-size: var(--fs-sm);
    }

    li {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      color: var(--color-text-muted);
    }

    li.met {
      color: var(--status-resolved-bg);
    }
  `,
})
export class PasswordChecklist {
  readonly password = input('');

  protected readonly rules = PASSWORD_RULES;
  protected readonly min = PASSWORD_MIN;
  protected readonly max = PASSWORD_MAX;
  protected readonly checks = computed(() => checkPassword(this.password()));
  protected readonly metIcon = IconCircleCheck;
  protected readonly unmetIcon = IconCircle;
}
