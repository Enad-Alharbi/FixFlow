import { DIALOG_DATA, Dialog, DialogRef } from '@angular/cdk/dialog';
import { Component, Injectable, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { Button } from '../button/button';

/**
 * The frame of every dialog: title, body, actions. Opening, focus trapping, Escape, the backdrop, and restoring
 * focus afterwards come from the Angular CDK Dialog (headless); this component is only FixFlow's look.
 *   <app-dialog [title]="…"> body… <div dialogActions> buttons… </div> </app-dialog>
 */
@Component({
  selector: 'app-dialog',
  template: `
    <h2 class="dialog-title">{{ title() }}</h2>
    <div class="dialog-body"><ng-content /></div>
    <div class="dialog-actions"><ng-content select="[dialogActions]" /></div>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-4);
      inline-size: min(32rem, calc(100vi - var(--space-7)));
      padding: var(--space-6);
      border-radius: var(--radius-lg);
      background: var(--color-surface);
      box-shadow: var(--shadow-overlay);
    }

    .dialog-title {
      font-size: var(--fs-xl);
    }

    .dialog-body {
      color: var(--color-text-muted);
    }

    .dialog-actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--space-3);
    }
  `,
  host: { role: 'document' },
})
export class DialogFrame {
  readonly title = input.required<string>();
}

export interface ConfirmData {
  title: string;
  message: string;
  confirm: string;
  /** Destructive or disruptive actions get the danger style on the confirm button. */
  danger?: boolean;
}

/** "Are you sure?" with the consequence spelled out. Texts arrive already translated. */
@Component({
  selector: 'app-confirm-dialog',
  imports: [DialogFrame, Button, TranslocoDirective],
  template: `
    <app-dialog [title]="data.title" *transloco="let t">
      <p>{{ data.message }}</p>
      <div dialogActions>
        <button appButton type="button" (click)="ref.close(false)">{{ t('common.cancel') }}</button>
        <button
          appButton
          type="button"
          [variant]="data.danger ? 'danger' : 'primary'"
          (click)="ref.close(true)"
        >
          {{ data.confirm }}
        </button>
      </div>
    </app-dialog>
  `,
  styles: `
    p {
      margin: 0;
    }
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
}

/** const ok = await confirm.ask({ title, message, confirm: 'Suspend', danger: true }); */
@Injectable({ providedIn: 'root' })
export class Confirm {
  private readonly dialog = inject(Dialog);

  async ask(data: ConfirmData): Promise<boolean> {
    const ref = this.dialog.open<boolean>(ConfirmDialog, {
      data,
      role: 'alertdialog',
      ariaLabel: data.title, // the dialog's accessible name, read out when it opens
      backdropClass: 'app-dialog-backdrop',
    });
    return (await firstValueFrom(ref.closed)) === true;
  }
}
