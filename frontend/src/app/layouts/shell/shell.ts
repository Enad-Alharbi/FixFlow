import { Component, ElementRef, computed, inject, input, viewChild } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { IconLogout, TablerIconComponent } from '@tabler/icons-angular';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageSwitcher } from '../../core/i18n/language-switcher';
import { Toaster } from '../../shared/ui/toast/toast';

export type Plane = 'tenant' | 'platform';

interface NavItem {
  path: string;
  labelKey: string;
  /** Active only on exactly this URL (a home link would otherwise be active everywhere). */
  exact?: boolean;
}

/**
 * Navigation per plane. Only pages that exist are listed; each feature adds its entry in its own phase.
 * Paths are absolute so links work from any child route.
 */
const NAV: Record<Plane, NavItem[]> = {
  tenant: [
    { path: '/', labelKey: 'nav.systemStatus', exact: true },
    // Development builds only (same switch as the route in app.routes.ts).
    ...(typeof ngDevMode === 'undefined' || ngDevMode
      ? [{ path: '/dev/style-guide', labelKey: 'nav.styleGuide' }]
      : []),
  ],
  platform: [
    { path: '/platform/dashboard', labelKey: 'nav.dashboard' },
    { path: '/platform/tenants', labelKey: 'nav.tenants' }, // also active on a tenant's own pages
    { path: '/platform/users', labelKey: 'nav.users' },
    { path: '/platform/system', labelKey: 'nav.systemStatus' },
  ],
};

/**
 * The frame around every signed-in page: top bar + main content.
 * One component serves both planes; the Control Panel ("platform") gets the tool-black bar and the
 * persistent Platform tag so the admin always knows they act at platform level (CLAUDE.md §10).
 * The plane comes from route data (see app.routes.ts) through router input binding.
 */
@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    TranslocoDirective,
    TablerIconComponent,
    LanguageSwitcher,
    Toaster,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  host: { '[class.platform]': "plane() === 'platform'" },
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly signOutIcon = IconLogout;

  readonly plane = input<Plane>('tenant');
  protected readonly nav = computed(() => NAV[this.plane()]);
  protected readonly home = computed(() => (this.plane() === 'platform' ? '/platform' : '/'));

  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  protected signOut(): Promise<void> {
    return this.auth.signOut();
  }

  /** Skip link: move focus to the content without changing the URL (a plain #main link would re-route). */
  protected skipToMain(event: Event): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }
}
