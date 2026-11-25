import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { planeGuard, signedOutGuard } from './auth.guards';
import { authInterceptor } from './auth.interceptor';
import { AUTH_URL, AuthService, CurrentUser } from './auth.service';

const API = `${environment.apiUrl}/api`;

function session(token: string, user: Partial<CurrentUser> = {}) {
  return {
    accessToken: token,
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: {
      id: 'u1',
      userName: 'acme.supervisor',
      email: 'a@acme.example',
      displayName: 'Alice',
      role: 'Supervisor',
      tenant: {
        id: 't1',
        name: 'Acme',
        code: 'acme',
        defaultLanguage: 'en',
        timeZone: 'Asia/Riyadh',
      },
      ...user,
    },
  };
}

describe('auth', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  async function signIn(token = 'token-1', user: Partial<CurrentUser> = {}) {
    const done = auth.signIn('acme.supervisor', 'secret');
    const request = http.expectOne(`${AUTH_URL}/sign-in`);
    expect(request.request.withCredentials).toBe(true); // so the browser stores the refresh cookie
    request.flush(session(token, user));
    await done;
  }

  it('keeps the session in memory only, never in web storage', async () => {
    localStorage.clear();
    sessionStorage.clear();

    await signIn();

    expect(auth.user()?.displayName).toBe('Alice');
    expect(auth.accessToken()).toBe('token-1');
    expect(localStorage.length + sessionStorage.length).toBe(0);
  });

  it('attaches the token to API calls but never to other sites', async () => {
    await signIn();
    const client = TestBed.inject(HttpClient);

    client.get(`${API}/anything`).subscribe();
    client.get('https://other.example/data').subscribe();

    expect(http.expectOne(`${API}/anything`).request.headers.get('Authorization')).toBe(
      'Bearer token-1',
    );
    expect(http.expectOne('https://other.example/data').request.headers.has('Authorization')).toBe(
      false,
    );
  });

  it('on 401 refreshes once and retries with the new token', async () => {
    await signIn();
    const result = firstValueFrom(
      TestBed.inject(HttpClient).get<{ ok: boolean }>(`${API}/anything`),
    );

    http.expectOne(`${API}/anything`).flush(null, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve(); // let the interceptor start the refresh
    const refresh = http.expectOne(`${AUTH_URL}/refresh`);
    expect(refresh.request.withCredentials).toBe(true);
    refresh.flush(session('token-2'));
    await vi.waitFor(() => http.expectOne(`${API}/anything`).flush({ ok: true }));

    expect(await result).toEqual({ ok: true });
    expect(auth.accessToken()).toBe('token-2');
  });

  it('ends the session and goes to sign-in when the refresh is refused', async () => {
    await signIn();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const result = firstValueFrom(TestBed.inject(HttpClient).get(`${API}/anything`));

    http.expectOne(`${API}/anything`).flush(null, { status: 401, statusText: 'Unauthorized' });
    await vi.waitFor(() =>
      http
        .expectOne(`${AUTH_URL}/refresh`)
        .flush({ code: 'auth.session_expired' }, { status: 401, statusText: '' }),
    );

    await expect(result).rejects.toBeTruthy();
    expect(auth.isSignedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/sign-in'], expect.anything());
  });

  it('shares one refresh between simultaneous callers (refresh tokens rotate)', async () => {
    const first = auth.refresh();
    const second = auth.refresh();

    await vi.waitFor(() => http.expectOne(`${AUTH_URL}/refresh`).flush(session('token-3')));

    expect(await first).toBe('token-3');
    expect(await second).toBe('token-3');
  });

  it('restore() with no refresh cookie leaves the user signed out without throwing', async () => {
    const restored = auth.restore();
    await vi.waitFor(() =>
      http.expectOne(`${AUTH_URL}/refresh`).flush(null, { status: 401, statusText: '' }),
    );

    await restored;
    expect(auth.isSignedIn()).toBe(false);
  });

  describe('guards', () => {
    const state = (url: string) => ({ url }) as RouterStateSnapshot;
    const run = (guard: ReturnType<typeof planeGuard>, url = '/') =>
      TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, state(url))) as
        boolean | UrlTree;
    const asUrl = (result: boolean | UrlTree) =>
      result instanceof UrlTree ? result.toString() : result;

    it('sends signed-out visitors to sign-in, remembering the page', () => {
      expect(asUrl(run(planeGuard('tenant'), '/dev/style-guide'))).toBe(
        '/sign-in?returnUrl=%2Fdev%2Fstyle-guide',
      );
    });

    it('keeps tenant users out of the Control Panel and the Platform Admin out of the tenant app', async () => {
      await signIn();
      expect(run(planeGuard('tenant'))).toBe(true);
      expect(asUrl(run(planeGuard('platform')))).toBe('/');

      await signIn('admin-token', { role: 'PlatformAdmin', tenant: null });
      expect(run(planeGuard('platform'))).toBe(true);
      expect(asUrl(run(planeGuard('tenant')))).toBe('/platform');
    });

    it('sends a signed-in user away from the sign-in page', async () => {
      expect(run(signedOutGuard)).toBe(true);
      await signIn();
      expect(asUrl(run(signedOutGuard))).toBe('/');
    });
  });
});
