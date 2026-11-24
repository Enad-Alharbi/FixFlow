import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { LanguageSwitcher } from './core/i18n/language-switcher';

// shortcut: the switcher sits above the page until the real shells (top bars) arrive in step 2e.
@Component({
  imports: [RouterOutlet, LanguageSwitcher],
  selector: 'app-root',
  template: `
    <div class="temp-bar"><app-language-switcher /></div>
    <router-outlet />
  `,
  styles: `
    .temp-bar {
      display: flex;
      justify-content: flex-end;
      padding: var(--space-3) var(--space-4);
    }
  `,
})
export class App {}
