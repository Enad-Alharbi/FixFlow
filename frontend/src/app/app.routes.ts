import { Route, Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';
import { planeGuard, signedOutGuard } from './core/auth/auth.guards';

// Feature strings live in public/i18n/system-status/ and load with this route (alias: systemStatus).
const systemStatus: Route = {
  path: '',
  title: 'titles.systemStatus',
  providers: [provideTranslocoScope('system-status')],
  loadComponent: () => import('./features/system-status/system-status').then((m) => m.SystemStatus),
};

// Development only. Production builds define ngDevMode as false, so this route and its chunk are removed.
const devRoutes: Route[] =
  typeof ngDevMode === 'undefined' || ngDevMode
    ? [
        {
          path: 'dev/style-guide',
          title: 'titles.styleGuide',
          providers: [provideTranslocoScope('dev')],
          loadComponent: () =>
            import('./features/dev/style-guide/style-guide').then((m) => m.StyleGuide),
        },
      ]
    : [];

const shell = () => import('./layouts/shell/shell').then((m) => m.Shell);

// `title` values are translation keys (TranslatedTitleStrategy). Guards only route people; the API enforces access.
export const routes: Routes = [
  {
    path: 'sign-in',
    canActivate: [signedOutGuard],
    loadComponent: () => import('./layouts/auth-layout/auth-layout').then((m) => m.AuthLayout),
    children: [
      {
        path: '',
        title: 'titles.signIn',
        providers: [provideTranslocoScope('auth')],
        loadComponent: () => import('./features/auth/sign-in/sign-in').then((m) => m.SignIn),
      },
    ],
  },
  // Control Panel: its own lazy area under /platform, Platform Admin only (CLAUDE.md §10).
  {
    path: 'platform',
    canActivate: [planeGuard('platform')],
    loadComponent: shell,
    data: { plane: 'platform' },
    children: [
      { ...systemStatus, path: 'system' },
      {
        path: '',
        // Control Panel strings (alias: platform), loaded once for every page below.
        providers: [provideTranslocoScope('platform')],
        loadChildren: () =>
          import('./features/platform/platform.routes').then((m) => m.platformRoutes),
      },
    ],
  },
  // Tenant app: tenant users only.
  {
    path: '',
    canActivate: [planeGuard('tenant')],
    loadComponent: shell,
    data: { plane: 'tenant' },
    children: [...devRoutes, systemStatus],
  },
  { path: '**', redirectTo: '' },
];
