import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Plane } from '../../layouts/shell/shell';
import { AuthService } from './auth.service';

/**
 * Route guards improve the experience only: they send people to the right place.
 * The API enforces every rule (policies + tenant isolation) whatever the browser does (CLAUDE.md §7.3).
 */

/** The tenant app needs a tenant user; the Control Panel needs the Platform Admin. Others are redirected. */
export function planeGuard(plane: Plane): CanActivateFn {
  return (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.isSignedIn())
      return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
    const allowed = plane === 'platform' ? auth.isPlatformAdmin() : !auth.isPlatformAdmin();
    return allowed || router.parseUrl(auth.homeUrl());
  };
}

/** The sign-in page is for signed-out visitors; a signed-in user goes to their app. */
export const signedOutGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return !auth.isSignedIn() || inject(Router).parseUrl(auth.homeUrl());
};
