import { Routes } from '@angular/router';

// Control Panel pages (under /platform). `title` values are translation keys (TranslatedTitleStrategy).
export const platformRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'tenants' },
  {
    path: 'tenants',
    title: 'titles.tenants',
    loadComponent: () => import('./tenants/tenant-list').then((m) => m.TenantList),
  },
  {
    path: 'tenants/new',
    title: 'titles.newTenant',
    loadComponent: () => import('./tenants/tenant-create').then((m) => m.TenantCreate),
  },
  {
    path: 'tenants/:id',
    title: 'titles.tenant',
    loadComponent: () => import('./tenants/tenant-detail').then((m) => m.TenantDetail),
  },
];
