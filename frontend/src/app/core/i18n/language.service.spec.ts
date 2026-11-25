import { Directionality } from '@angular/cdk/bidi';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { AuthService } from '../auth/auth.service';
import { LanguageService } from './language.service';
import { LocalDatePipe } from './local-date.pipe';
import { provideTestTranslations } from './testing';

describe('LanguageService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideTestTranslations()] });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    document.documentElement.removeAttribute('lang');
    document.documentElement.removeAttribute('dir');
  });

  it('switching to Arabic updates Transloco, <html lang dir>, CDK direction, and the saved choice', async () => {
    const service = TestBed.inject(LanguageService);

    await service.use('ar');

    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('ar');
    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(TestBed.inject(Directionality).value).toBe('rtl');
    expect(localStorage.getItem('fixflow.language')).toBe('ar');
  });

  it('starts with the saved choice even when the browser prefers another language', async () => {
    localStorage.setItem('fixflow.language', 'en');
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('ar-SA');

    await TestBed.inject(LanguageService).init();

    expect(document.documentElement.lang).toBe('en');
  });

  it('starts in Arabic for an Arabic browser when nothing is saved, without saving it', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('ar-SA');

    await TestBed.inject(LanguageService).init();

    expect(document.documentElement.dir).toBe('rtl');
    expect(localStorage.getItem('fixflow.language')).toBeNull();
  });

  it('falls back to English for other browser languages', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('fr-FR');

    await TestBed.inject(LanguageService).init();

    expect(document.documentElement.lang).toBe('en');
  });
});

describe('LocalDatePipe', () => {
  const morning = new Date('2026-11-24T05:12:00Z');

  beforeEach(() =>
    TestBed.configureTestingModule({ providers: [provideTestTranslations(), LocalDatePipe] }),
  );

  it('formats Arabic dates with Western digits only (never ٠-٩)', async () => {
    await TestBed.inject(LanguageService).use('ar');

    const text = TestBed.inject(LocalDatePipe).transform(morning, 'date');

    expect(text).toMatch(/2026/);
    expect(text).not.toMatch(/[٠-٩۰-۹]/);
  });

  it('re-formats when the language changes', async () => {
    const language = TestBed.inject(LanguageService);
    const pipe = TestBed.inject(LocalDatePipe);

    await language.use('en');
    const english = pipe.transform(morning, 'date');
    await language.use('ar');
    const arabic = pipe.transform(morning, 'date');

    expect(english).toContain('Nov');
    expect(arabic).not.toBe(english);
  });

  it("shows times in the signed-in user's tenant time zone, whatever the browser's zone", async () => {
    TestBed.overrideProvider(AuthService, {
      useValue: { user: () => ({ tenant: { timeZone: 'Asia/Riyadh' } }) }, // UTC+3, no daylight saving
    });
    await TestBed.inject(LanguageService).use('en');

    expect(TestBed.inject(LocalDatePipe).transform(morning, 'time')).toBe('08:12'); // 05:12 UTC
  });
});

describe('LanguageService.init with a tenant default', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideTestTranslations()] });
  });

  it("uses the tenant's default language when the user has made no choice on this device", async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US');

    await TestBed.inject(LanguageService).init('ar');

    expect(document.documentElement.lang).toBe('ar');
  });

  it("keeps the user's own saved choice over the tenant default", async () => {
    localStorage.setItem('fixflow.language', 'en');

    await TestBed.inject(LanguageService).init('ar');

    expect(document.documentElement.lang).toBe('en');
  });
});
