import { Route, Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';

// Feature strings live in public/i18n/system-status/ and load with this route (alias: systemStatus).
const systemStatus: Route = {
  path: '',
  providers: [provideTranslocoScope('system-status')],
  loadComponent: () => import('./features/system-status/system-status').then((m) => m.SystemStatus),
};

// Development only. Production builds define ngDevMode as false, so this route and its chunk are removed.
const devRoutes: Route[] =
  typeof ngDevMode === 'undefined' || ngDevMode
    ? [
        {
          path: 'dev/style-guide',
          providers: [provideTranslocoScope('dev')],
          loadComponent: () =>
            import('./features/dev/style-guide/style-guide').then((m) => m.StyleGuide),
        },
      ]
    : [];

const shell = () => import('./layouts/shell/shell').then((m) => m.Shell);

// Guards (signed in, PlatformAdmin vs tenant role) are added in Phase 3; the API enforces access regardless.
export const routes: Routes = [
  // Control Panel: its own lazy area under /platform (CLAUDE.md §10).
  {
    path: 'platform',
    loadComponent: shell,
    data: { plane: 'platform' },
    children: [systemStatus],
  },
  // Tenant app.
  {
    path: '',
    loadComponent: shell,
    data: { plane: 'tenant' },
    children: [...devRoutes, systemStatus],
  },
  { path: '**', redirectTo: '' },
];
