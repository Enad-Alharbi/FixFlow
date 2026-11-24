import { Component, computed, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from './language.service';

/** Offers the other language, named in that language ("العربية" while in English, "English" while in Arabic). */
@Component({
  selector: 'app-language-switcher',
  imports: [TranslocoDirective],
  template: `
    <button
      *transloco="let t"
      type="button"
      class="switch"
      [attr.lang]="target()"
      (click)="language.use(target())"
    >
      {{ t('language.names.' + target()) }}
    </button>
  `,
  styles: `
    .switch {
      min-block-size: var(--space-9);
      padding-inline: var(--space-3);
      border: var(--border-width) solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface);
      font-family: var(--font-display);
      font-weight: var(--fw-semibold);
      cursor: pointer;
    }

    .switch:hover {
      border-color: var(--color-text-muted);
    }

    .switch:lang(ar) {
      font-family: var(--font-arabic);
      font-size: var(--fs-sm);
    }
  `,
})
export class LanguageSwitcher {
  protected readonly language = inject(LanguageService);
  protected readonly target = computed(() => (this.language.language() === 'ar' ? 'en' : 'ar'));
}
