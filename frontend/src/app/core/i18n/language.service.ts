import { Directionality, Direction } from '@angular/cdk/bidi';
import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

export type Language = 'en' | 'ar';

const STORAGE_KEY = 'fixflow.language';

/** Intl locale per language. `-u-nu-latn` forces Western digits (0-9) in Arabic (CLAUDE.md §13.1). */
export const INTL_LOCALES: Record<Language, string> = { en: 'en-GB', ar: 'ar-u-nu-latn' };

/**
 * The one place that changes the UI language. Keeps four things in step:
 * Transloco's active language, <html lang dir>, the CDK Directionality (overlays, menus), and the saved preference.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly directionality = inject(Directionality);
  private readonly document = inject(DOCUMENT);

  private readonly current = signal<Language>('en');
  readonly language = this.current.asReadonly();
  readonly direction = computed<Direction>(() => (this.current() === 'ar' ? 'rtl' : 'ltr'));
  readonly locale = computed(() => INTL_LOCALES[this.current()]);

  /**
   * Picks the starting language and loads its root strings before the first render (no flash of keys).
   * Order today: saved choice -> browser language -> English.
   * User preference and tenant default are added in front once sign-in exists (Phase 3/5).
   */
  async init(): Promise<void> {
    await this.use(this.saved() ?? this.fromBrowser() ?? 'en', { persist: false });
  }

  async use(language: Language, { persist = true } = {}): Promise<void> {
    await firstValueFrom(this.transloco.load(language));
    this.transloco.setActiveLang(language);
    this.current.set(language);

    const html = this.document.documentElement;
    html.lang = language;
    html.dir = this.direction();

    this.directionality.valueSignal.set(this.direction());
    this.directionality.change.emit(this.direction());

    if (persist) this.save(language);
  }

  private saved(): Language | null {
    try {
      const value = localStorage.getItem(STORAGE_KEY);
      return value === 'en' || value === 'ar' ? value : null;
    } catch {
      return null; // storage blocked (private mode, policy): fall through to the next source
    }
  }

  private save(language: Language): void {
    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Not fatal: the choice simply isn't remembered.
    }
  }

  private fromBrowser(): Language | null {
    const browser = this.document.defaultView?.navigator.language ?? '';
    return browser.toLowerCase().startsWith('ar') ? 'ar' : null;
  }
}
