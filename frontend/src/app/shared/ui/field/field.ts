import { Component, Directive, ViewEncapsulation, computed, inject, input } from '@angular/core';
import { IconAlertCircle, TablerIconComponent } from '@tabler/icons-angular';

let nextId = 0;

/**
 * A labelled form field: label, the projected native control, an optional hint, and an error message.
 * The native <input appInput> stays in the page (keyboard, autofill, password managers all keep working);
 * this wrapper only links label, hint and error to it for screen readers.
 *
 *   <app-field [label]="t('login')" [error]="loginError()">
 *     <input appInput formControlName="login" autocomplete="username" />
 *     <button fieldEnd ...>…</button>      (optional: sits after the input, e.g. show password)
 *   </app-field>
 */
@Component({
  selector: 'app-field',
  imports: [TablerIconComponent],
  template: `
    <label class="field-label" [for]="controlId">{{ label() }}</label>
    <div class="field-control">
      <ng-content />
      <ng-content select="[fieldEnd]" />
    </div>
    @if (hint()) {
      <p class="field-hint" [id]="hintId">{{ hint() }}</p>
    }
    @if (error()) {
      <p class="field-error" [id]="errorId">
        <tabler-icon [icon]="errorIcon" aria-hidden="true" />{{ error() }}
      </p>
    }
  `,
  styleUrl: './field.scss',
  // Unscoped so the styles reach the projected <input>; every selector is prefixed with app-field.
  encapsulation: ViewEncapsulation.None,
})
export class Field {
  readonly label = input.required<string>();
  readonly hint = input<string | null>(null);
  /** Translated error text, or null. Show it only once the user has touched the field or submitted. */
  readonly error = input<string | null>(null);

  readonly controlId = `field-${++nextId}`;
  readonly hintId = `${this.controlId}-hint`;
  readonly errorId = `${this.controlId}-error`;
  readonly describedBy = computed(
    () =>
      [this.hint() && this.hintId, this.error() && this.errorId].filter(Boolean).join(' ') || null,
  );
  protected readonly errorIcon = IconAlertCircle;
}

/**
 * Marks the native control inside <app-field> (input, textarea, or select: a native <select> keeps the phone's own
 * picker and full keyboard support, which a custom dropdown would have to rebuild).
 * Wires its id, aria-describedby and aria-invalid.
 */
@Directive({
  selector: 'input[appInput], textarea[appInput], select[appInput]',
  host: {
    class: 'field-input',
    '[id]': 'field.controlId',
    '[attr.aria-describedby]': 'field.describedBy()',
    '[attr.aria-invalid]': "field.error() ? 'true' : null",
  },
})
export class FieldInput {
  protected readonly field = inject(Field);
}
