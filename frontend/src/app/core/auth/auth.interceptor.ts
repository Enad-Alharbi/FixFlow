import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AUTH_URL, AuthService } from './auth.service';

/**
 * Attaches the access token to API calls. On a 401 it refreshes once and retries; if that fails the session is over.
 * The token is sent only to our own API (never to a third-party URL).
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const isApi = request.url.startsWith(`${environment.apiUrl}/api/`);
  const isAuthEndpoint = request.url.startsWith(`${AUTH_URL}/`) && !request.url.endsWith('/me');
  if (!isApi || isAuthEndpoint) return next(request); // sign-in/refresh/sign-out use the cookie, not the token

  const token = auth.accessToken();
  return next(withToken(request, token)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !token)
        return throwError(() => error);

      // The access token expired (15 minutes): get a new one, then retry this request once.
      return from(auth.refresh()).pipe(
        switchMap((fresh) => {
          if (fresh) return next(withToken(request, fresh));
          auth.end(router.url);
          return throwError(() => error);
        }),
      );
    }),
  );
};

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}
