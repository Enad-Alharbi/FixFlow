import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageSwitcher } from '../../core/i18n/language-switcher';

/** The frame for signed-out pages: brand and language switch on top, the page centred on paper. */
@Component({
  selector: 'app-auth-layout',
  imports: [RouterOutlet, TranslocoDirective, LanguageSwitcher],
  template: `
    <header class="top" *transloco="let t">
      <span class="brand">{{ t('app.name') }}</span>
      <app-language-switcher />
    </header>
    <main id="main">
      <router-outlet />
    </main>
  `,
  styles: `
    :host {
      display: grid;
      grid-template-rows: auto 1fr;
      min-block-size: 100dvb;
    }

    .top {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: var(--space-3) var(--space-4);
    }

    .brand {
      font-family: var(--font-display);
      font-size: var(--fs-xl);
      font-weight: var(--fw-bold);
    }

    // minmax(0, …): an "auto" column would grow to the inputs' minimum width and overflow phones.
    main {
      display: grid;
      grid-template-columns: minmax(0, 28rem);
      justify-content: center;
      align-content: start;
      padding: var(--space-6) var(--space-4) var(--space-12);
    }
  `,
})
export class AuthLayout {}
