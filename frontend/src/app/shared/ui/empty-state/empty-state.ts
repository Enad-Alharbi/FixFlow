import { Component, input } from '@angular/core';
import { TablerIcon, TablerIconComponent } from '@tabler/icons-angular';

/**
 * Empty state (design-direction.md §7): a dashed blank tag with its punched hole, an icon, one sentence, and
 * at most one action, projected:  <app-empty-state [icon]="…" [message]="…"><a appButton …>…</a></app-empty-state>
 */
@Component({
  selector: 'app-empty-state',
  imports: [TablerIconComponent],
  template: `
    <span class="hole" aria-hidden="true"></span>
    <tabler-icon class="icon" [icon]="icon()" aria-hidden="true" />
    <p>{{ message() }}</p>
    <ng-content />
  `,
  styles: `
    :host {
      position: relative;
      display: grid;
      justify-items: center;
      gap: var(--space-3);
      padding: var(--space-8) var(--space-6);
      border: var(--border-width-strong) dashed var(--color-border);
      color: var(--color-text-muted);
      text-align: center;
    }

    .hole {
      position: absolute;
      inset-inline-start: var(--space-4);
      inset-block-start: 50%;
      inline-size: var(--space-4);
      block-size: var(--space-4);
      border: var(--border-width-strong) dashed var(--color-border);
      border-radius: var(--radius-round);
      translate: 0 -50%;
    }

    .icon {
      font-size: var(--fs-3xl);
    }

    p {
      margin: 0;
      max-inline-size: 32ch;
      color: var(--color-text);
      font-size: var(--fs-lg);
    }
  `,
})
export class EmptyState {
  readonly icon = input.required<TablerIcon>();
  readonly message = input.required<string>();
}
