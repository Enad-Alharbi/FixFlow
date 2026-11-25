import { Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

/**
 * Browser tab titles from route data: `title: 'titles.signIn'` is a translation key, shown as "Sign in · FixFlow".
 * Re-applied when the language changes, so the tab follows the UI language. Keys live in the root files (titles.*).
 */
@Injectable({ providedIn: 'root' })
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);
  private key: string | undefined;

  constructor() {
    super();
    this.transloco.langChanges$.pipe(takeUntilDestroyed()).subscribe(() => this.apply());
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.key = this.buildTitle(snapshot);
    this.apply();
  }

  private apply(): void {
    const app = this.transloco.translate('app.name');
    if (app === 'app.name') return; // strings not loaded yet (first language emit): keep index.html's title
    this.title.setTitle(this.key ? `${this.transloco.translate(this.key)} · ${app}` : app);
  }
}
