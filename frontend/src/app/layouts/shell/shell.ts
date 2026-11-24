import { Component, ElementRef, computed, input, viewChild } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageSwitcher } from '../../core/i18n/language-switcher';

export type Plane = 'tenant' | 'platform';

interface NavItem {
  path: string;
  labelKey: string;
}

/**
 * Navigation per plane. Only pages that exist are listed; each feature adds its entry in its own phase.
 * Paths are absolute so links work from any child route.
 */
const NAV: Record<Plane, NavItem[]> = {
  tenant: [{ path: '/', labelKey: 'nav.systemStatus' }],
  platform: [{ path: '/platform', labelKey: 'nav.systemStatus' }],
};

/**
 * The frame around every signed-in page: top bar + main content.
 * One component serves both planes; the Control Panel ("platform") gets the tool-black bar and the
 * persistent Platform tag so the admin always knows they act at platform level (CLAUDE.md §10).
 * The plane comes from route data (see app.routes.ts) through router input binding.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoDirective, LanguageSwitcher],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  host: { '[class.platform]': "plane() === 'platform'" },
})
export class Shell {
  readonly plane = input<Plane>('tenant');
  protected readonly nav = computed(() => NAV[this.plane()]);
  protected readonly home = computed(() => (this.plane() === 'platform' ? '/platform' : '/'));

  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  /** Skip link: move focus to the content without changing the URL (a plain #main link would re-route). */
  protected skipToMain(event: Event): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }
}
