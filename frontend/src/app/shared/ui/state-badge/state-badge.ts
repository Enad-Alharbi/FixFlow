import { Component, input } from '@angular/core';
import { TablerIcon, TablerIconComponent } from '@tabler/icons-angular';

/** Safety-sign meanings, shared by tenant and account states: go, caution, off, stop. */
export type StateTone = 'ok' | 'warn' | 'off' | 'danger';

/**
 * A small state label: tone colour + icon + text, never colour alone.
 *   <app-state-badge tone="warn" [icon]="pauseIcon">Suspended</app-state-badge>
 */
@Component({
  selector: 'app-state-badge',
  imports: [TablerIconComponent],
  template: `<tabler-icon [icon]="icon()" aria-hidden="true" /><ng-content />`,
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

    :host([data-tone='ok']) {
      --badge-bg: var(--state-ok-bg);
      --badge-fg: var(--state-ok-fg);
    }

    :host([data-tone='warn']) {
      --badge-bg: var(--state-warn-bg);
      --badge-fg: var(--state-warn-fg);
    }

    :host([data-tone='off']) {
      --badge-bg: var(--state-off-bg);
      --badge-fg: var(--state-off-fg);
    }

    :host([data-tone='danger']) {
      --badge-bg: var(--color-danger);
      --badge-fg: var(--color-on-danger);
    }
  `,
  host: { '[attr.data-tone]': 'tone()' },
})
export class StateBadge {
  readonly tone = input.required<StateTone>();
  readonly icon = input.required<TablerIcon>();
}
