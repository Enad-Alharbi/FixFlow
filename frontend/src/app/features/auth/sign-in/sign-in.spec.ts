import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService, CurrentUser } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { provideTestTranslations } from '../../../core/i18n/testing';
import { SignIn } from './sign-in';

const supervisor = { role: 'Supervisor', tenant: { defaultLanguage: 'ar' } } as CurrentUser;

describe('SignIn', () => {
  // A fresh fake per test, so no test sees another's resolved or rejected value.
  let auth: { signIn: ReturnType<typeof vi.fn>; homeUrl: () => string; user: () => null };
  beforeEach(() => (auth = { signIn: vi.fn(), homeUrl: () => '/', user: () => null }));

  async function render(returnUrl?: string) {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [SignIn],
      providers: [
        provideTestTranslations(),
        provideRouter([]),
        { provide: AuthService, useValue: auth },
      ],
    });
    const fixture = TestBed.createComponent(SignIn);
    if (returnUrl) fixture.componentRef.setInput('returnUrl', returnUrl);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const type = (index: number, value: string) => {
      const input = el.querySelectorAll('input')[index];
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const submit = async () => {
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
    };
    return { fixture, el, navigate, type, submit };
  }

  afterEach(() => {
    document.documentElement.removeAttribute('lang');
    document.documentElement.removeAttribute('dir');
  });

  it('labels each input and links its error message for screen readers', async () => {
    const { el, submit } = await render();

    await submit();

    const login = el.querySelector('input')!;
    expect(el.querySelector(`label[for="${login.id}"]`)?.textContent).toContain(
      'Email or username',
    );
    expect(login.getAttribute('aria-invalid')).toBe('true');
    expect(document.getElementById(login.getAttribute('aria-describedby')!)?.textContent).toContain(
      'Enter your email or username.',
    );
    expect(auth.signIn).not.toHaveBeenCalled();
  });

  it('signs in, applies the tenant language, and opens the page the user asked for', async () => {
    auth.signIn.mockResolvedValue(supervisor);
    const { type, submit, navigate } = await render('/dev/style-guide');

    type(0, '  acme.supervisor');
    type(1, 'Valid#Pass1');
    await submit();

    expect(auth.signIn).toHaveBeenCalledWith('  acme.supervisor', 'Valid#Pass1'); // the API trims
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/dev/style-guide'));
    expect(TestBed.inject(LanguageService).language()).toBe('ar');
  });

  it.each(['//evil.example', 'https://evil.example', '/sign-in'])(
    'ignores an unsafe returnUrl %j',
    async (url) => {
      auth.signIn.mockResolvedValue(supervisor);
      const { type, submit, navigate } = await render(url);

      type(0, 'acme.supervisor');
      type(1, 'Valid#Pass1');
      await submit();

      await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/'));
    },
  );

  it('shows the generic message for wrong credentials and clears the password', async () => {
    auth.signIn.mockRejectedValue(
      new HttpErrorResponse({ status: 401, error: { code: 'auth.invalid_credentials' } }),
    );
    const { el, type, submit } = await render();

    type(0, 'acme.supervisor');
    type(1, 'Wrong#Pass1');
    await submit();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      'The email, username, or password is incorrect.',
    );
    expect(el.querySelectorAll('input')[1].value).toBe('');
    expect(el.querySelector('.field-error')).toBeNull(); // only the server message, no 'Enter your password.'
  });

  it('tells a locked-out user when the lock ends, with Western digits in Arabic', async () => {
    auth.signIn.mockRejectedValue(
      new HttpErrorResponse({
        status: 401,
        error: { code: 'auth.account_locked', lockoutEnd: '2026-11-25T10:05:00+00:00' },
      }),
    );
    const { fixture, el, type, submit } = await render();
    await TestBed.inject(LanguageService).use('ar');

    type(0, 'acme.supervisor');
    type(1, 'Wrong#Pass1');
    await submit();
    await fixture.whenStable();

    const alert = el.querySelector('[role="alert"]')!.textContent!;
    expect(alert).toContain('حسابك مقفل مؤقتًا');
    expect(alert).toMatch(/[0-9]{1,2}:[0-9]{2}/);
    expect(alert).not.toMatch(/[٠-٩]/);
  });

  it('falls back to a generic message for an unknown error code', async () => {
    auth.signIn.mockRejectedValue(
      new HttpErrorResponse({ status: 418, error: { code: 'teapot.brewing' } }),
    );
    const { el, type, submit } = await render();

    type(0, 'x');
    type(1, 'y');
    await submit();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      'Something went wrong. Please try again.',
    );
  });
});
