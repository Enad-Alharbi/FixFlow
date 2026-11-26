import { Component, booleanAttribute, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { IconLoader2, TablerIconComponent } from '@tabler/icons-angular';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

/**
 * FixFlow button. An attribute on the native element, so keyboard, form, and disabled behaviour stay native:
 *   <button appButton variant="primary" [loading]="saving()">Save changes</button>
 * Icons are projected before the label: <button appButton><tabler-icon [icon]="..." aria-hidden="true" /> Label</button>
 * One primary button per view; the rest are secondary or ghost. Danger: destructive or disruptive actions.
 */
@Component({
  // eslint-disable-next-line @angular-eslint/component-selector -- attribute selector keeps the native <button>/<a>
  selector: 'button[appButton], a[appButton]',
  imports: [TablerIconComponent, TranslocoDirective],
  template: `
    @if (loading()) {
      <tabler-icon class="spinner" [icon]="spinner" aria-hidden="true" />
      <span class="visually-hidden" *transloco="let t">{{ t('common.loading') }}</span>
    }
    <ng-content />
  `,
  styleUrl: './button.scss',
  host: {
    '[class]': "'btn btn-' + variant()",
    '[class.loading]': 'loading()',
    // Not natively disabled while loading: keeps keyboard focus on the button and announces the busy state.
    '[attr.aria-busy]': 'loading() || null',
    '[attr.aria-disabled]': 'loading() || null',
  },
})
export class Button {
  readonly variant = input<ButtonVariant>('secondary');
  readonly loading = input(false, { transform: booleanAttribute });
  protected readonly spinner = IconLoader2;
}
