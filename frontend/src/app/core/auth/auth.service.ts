import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Language } from '../i18n/language.service';

export type UserRole = 'PlatformAdmin' | 'Requester' | 'Technician' | 'Supervisor';

export interface TenantSummary {
  id: string;
  name: string;
  code: string;
  defaultLanguage: Language;
  timeZone: string;
}

export interface CurrentUser {
  id: string;
  userName: string;
  email: string;
  displayName: string;
  role: UserRole;
  tenant: TenantSummary | null; // null for the Platform Admin
}

interface Session {
  accessToken: string;
  expiresAt: string;
  user: CurrentUser;
}

export const AUTH_URL = `${environment.apiUrl}/api/auth`;

/**
 * The signed-in session (ADR 0005).
 * The access token lives ONLY in this service's memory — never in localStorage — so a script injected into the page
 * can't carry it away. The refresh token is an HttpOnly cookie the browser sends to /api/auth; JavaScript never sees it.
 * A page reload therefore starts signed out and restores the session with one refresh call.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<Session | null>(null);
  private refreshing: Promise<string | null> | null = null;

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isSignedIn = computed(() => this.session() !== null);
  readonly isPlatformAdmin = computed(() => this.user()?.role === 'PlatformAdmin');
  /** Where this user's app starts: the Control Panel or the tenant app. */
  readonly homeUrl = computed(() => (this.isPlatformAdmin() ? '/platform' : '/'));

  accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  async signIn(login: string, password: string): Promise<CurrentUser> {
    const session = await firstValueFrom(
      this.http.post<Session>(
        `${AUTH_URL}/sign-in`,
        { login, password },
        { withCredentials: true },
      ),
    );
    this.session.set(session);
    return session.user;
  }

  /**
   * Gets a new access token with the refresh cookie, or null when the session is over.
   * Calls are shared: within this tab through one pending promise, and across tabs through a Web Lock — refresh
   * tokens rotate, so two tabs refreshing with the same cookie at once would look like token theft to the server.
   */
  refresh(): Promise<string | null> {
    this.refreshing ??= this.withRefreshLock(() => this.callRefresh()).finally(
      () => (this.refreshing = null),
    );
    return this.refreshing;
  }

  /** On app start: restore the session from the refresh cookie, if there is one. Never throws. */
  async restore(): Promise<void> {
    await this.refresh();
  }

  async signOut(): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${AUTH_URL}/sign-out`, null, { withCredentials: true }));
    } catch {
      // Still sign out locally: the server session expires on its own.
    }
    this.end();
  }

  /** Ends the session locally and goes to sign-in (keeping where the user was, so they can return). */
  end(returnUrl?: string): void {
    this.session.set(null);
    void this.router.navigate(['/sign-in'], { queryParams: returnUrl ? { returnUrl } : {} });
  }

  private async callRefresh(): Promise<string | null> {
    try {
      const session = await firstValueFrom(
        this.http.post<Session>(`${AUTH_URL}/refresh`, null, { withCredentials: true }),
      );
      this.session.set(session);
      return session.accessToken;
    } catch {
      this.session.set(null);
      return null;
    }
  }

  private withRefreshLock<T>(work: () => Promise<T>): Promise<T> {
    // Web Locks: one holder at a time across every tab of this origin. The next tab then sends the NEW cookie.
    const locks = typeof navigator !== 'undefined' ? navigator.locks : undefined;
    return locks ? locks.request('fixflow-refresh', work) : work();
  }
}
