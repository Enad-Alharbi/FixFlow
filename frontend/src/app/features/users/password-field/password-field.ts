import { Component, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  IconCopy,
  IconEye,
  IconEyeOff,
  IconWand,
  TablerIconComponent,
} from '@tabler/icons-angular';
import { generatePassword } from '../../../core/auth/password-generator';
import { Button } from '../../../shared/ui/button/button';
import { Field, FieldInput } from '../../../shared/ui/field/field';
import { PasswordChecklist } from '../../../shared/ui/password-checklist/password-checklist';
import { Toasts } from '../../../shared/ui/toast/toast';

/**
 * A new password for someone else (CLAUDE.md §7.4): typed or generated, show/hide, copy, and the live checklist.
 * Used by Create User and by an admin's Set Password.
 */
@Component({
  selector: 'app-password-field',
  imports: [
    ReactiveFormsModule,
    TranslocoDirective,
    TablerIconComponent,
    Button,
    Field,
    FieldInput,
    PasswordChecklist,
  ],
  template: `
    <div class="password" *transloco="let t">
      <app-field
        [label]="t('users.fields.password')"
        [hint]="t('users.fields.passwordHint')"
        [error]="error()"
      >
        <!-- autocomplete="new-password": the browser must not fill in the ADMIN's own saved password here. -->
        <input
          appInput
          [formControl]="control()"
          [type]="visible() ? 'text' : 'password'"
          autocomplete="new-password"
          spellcheck="false"
          dir="ltr"
        />
        <button
          fieldEnd
          type="button"
          appButton
          variant="ghost"
          [attr.aria-pressed]="visible()"
          [attr.aria-label]="t('users.showPassword')"
          (click)="visible.set(!visible())"
        >
          <tabler-icon [icon]="visible() ? icons.hide : icons.show" aria-hidden="true" />
        </button>
      </app-field>

      <div class="tools">
        <button appButton type="button" (click)="generate()">
          <tabler-icon [icon]="icons.generate" aria-hidden="true" />{{
            t('users.generatePassword')
          }}
        </button>
        <button
          appButton
          variant="ghost"
          type="button"
          [disabled]="!control().value"
          (click)="copy()"
        >
          <tabler-icon [icon]="icons.copy" aria-hidden="true" />{{ t('users.copyPassword') }}
        </button>
      </div>

      <app-password-checklist [password]="control().value" />
    </div>
  `,
  styles: `
    .password {
      display: grid;
      gap: var(--space-4);
    }

    .tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
})
export class PasswordField {
  private readonly toasts = inject(Toasts);

  readonly control = input.required<FormControl<string>>();
  /** Translated error text, or null. */
  readonly error = input<string | null>(null);

  protected readonly visible = signal(false);
  protected readonly icons = {
    show: IconEye,
    hide: IconEyeOff,
    generate: IconWand,
    copy: IconCopy,
  };

  /** Fills in a generated password and shows it, so the creator can read or copy it for the user. */
  protected generate(): void {
    this.control().setValue(generatePassword());
    this.control().markAsTouched();
    this.visible.set(true);
  }

  protected async copy(): Promise<void> {
    await navigator.clipboard.writeText(this.control().value);
    this.toasts.show('users.passwordCopied');
  }
}
