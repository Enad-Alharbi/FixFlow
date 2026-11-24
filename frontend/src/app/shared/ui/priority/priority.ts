import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  IconChevronDown,
  IconChevronUp,
  IconChevronsUp,
  IconEqual,
  TablerIcon,
  TablerIconComponent,
} from '@tabler/icons-angular';

export type Priority = 'low' | 'medium' | 'high' | 'critical';

const ICONS: Record<Priority, TablerIcon> = {
  low: IconChevronDown,
  medium: IconEqual,
  high: IconChevronUp,
  critical: IconChevronsUp,
};

/** Priority field: coloured marker bar at the start edge + icon + label (never colour alone). */
@Component({
  selector: 'app-priority',
  imports: [TablerIconComponent, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <span class="label">{{ t('priority.label') }}</span>
      <span class="value"
        ><tabler-icon [icon]="icon()" aria-hidden="true" />{{ t('priority.' + priority()) }}</span
      >
    </ng-container>
  `,
  styles: `
    :host {
      display: inline-grid;
      padding-inline-start: var(--space-3);
      border-inline-start: var(--space-1) solid var(--marker);
    }

    :host([data-priority='low']) {
      --marker: var(--priority-low);
    }

    :host([data-priority='medium']) {
      --marker: var(--priority-medium);
    }

    :host([data-priority='high']) {
      --marker: var(--priority-high);
    }

    :host([data-priority='critical']) {
      --marker: var(--priority-critical);
    }

    .label {
      color: var(--color-text-muted);
      font-size: var(--fs-sm);
    }

    .value {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      font-family: var(--font-display);
      font-size: var(--fs-lg);
      font-weight: var(--fw-semibold);
    }

    tabler-icon {
      color: var(--marker);
    }
  `,
  host: { '[attr.data-priority]': 'priority()' },
})
export class PriorityIndicator {
  readonly priority = input.required<Priority>();
  protected readonly icon = computed(() => ICONS[this.priority()]);
}
