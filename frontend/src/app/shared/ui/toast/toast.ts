import { Component, Injectable, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { IconCircleCheck, IconX, TablerIconComponent } from '@tabler/icons-angular';

export interface Toast {
  id: number;
  /** Translation key and its parameters, translated when shown (so a language switch updates it too). */
  key: string;
  params?: Record<string, unknown>;
}

const LIFETIME_MS = 5000;

/** Short confirmations after an action ("Tenant suspended."). Errors stay next to what failed, not in a toast. */
@Injectable({ providedIn: 'root' })
export class Toasts {
  readonly items = signal<Toast[]>([]);
  private nextId = 0;

  show(key: string, params?: Record<string, unknown>): void {
    const toast = { id: ++this.nextId, key, params };
    this.items.update((items) => [...items, toast]);
    setTimeout(() => this.dismiss(toast.id), LIFETIME_MS);
  }

  dismiss(id: number): void {
    this.items.update((items) => items.filter((t) => t.id !== id));
  }
}

/** Placed once in the shell. A polite live region, so screen readers announce each toast without interrupting. */
@Component({
  selector: 'app-toaster',
  imports: [TranslocoDirective, TablerIconComponent],
  template: `
    <div class="stack" role="status" aria-live="polite" *transloco="let t">
      @for (toast of toasts.items(); track toast.id) {
        <p class="toast">
          <tabler-icon [icon]="icons.done" aria-hidden="true" />
          <span>{{ t(toast.key, toast.params) }}</span>
          <button
            type="button"
            class="close"
            [attr.aria-label]="t('common.dismiss')"
            (click)="toasts.dismiss(toast.id)"
          >
            <tabler-icon [icon]="icons.close" aria-hidden="true" />
          </button>
        </p>
      }
    </div>
  `,
  styles: `
    .stack {
      position: fixed;
      inset-block-end: var(--space-4);
      inset-inline-end: var(--space-4);
      z-index: var(--z-toast);
      display: grid;
      gap: var(--space-2);
      max-inline-size: calc(100vi - var(--space-7));
    }

    .toast {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin: 0;
      padding-block: var(--space-3);
      padding-inline: var(--space-4) var(--space-3);
      border-inline-start: var(--space-1) solid var(--tenant-active-bg);
      border-radius: var(--radius-md);
      background: var(--color-text);
      color: var(--color-surface);
      box-shadow: var(--shadow-overlay);
    }

    .close {
      display: inline-flex;
      padding: var(--space-1);
      border: 0;
      background: transparent;
      color: inherit;
      cursor: pointer;
    }
  `,
})
export class Toaster {
  protected readonly toasts = inject(Toasts);
  protected readonly icons = { done: IconCircleCheck, close: IconX };
}
