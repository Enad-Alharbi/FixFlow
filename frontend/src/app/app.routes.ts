import { Routes } from '@angular/router';
import { provideTranslocoScope } from '@jsverse/transloco';

export const routes: Routes = [
  {
    path: '',
    // Feature strings live in public/i18n/system-status/ and load with this route (alias: systemStatus).
    providers: [provideTranslocoScope('system-status')],
    loadComponent: () =>
      import('./features/system-status/system-status').then((m) => m.SystemStatus),
  },
];
