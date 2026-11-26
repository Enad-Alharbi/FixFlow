import { Routes } from '@angular/router';

// Control Panel pages (under /platform). `title` values are translation keys (TranslatedTitleStrategy).
export const platformRoutes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'dashboard',
    title: 'titles.dashboard',
    loadComponent: () => import('./dashboard/dashboard').then((m) => m.Dashboard),
  },
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
  {
    // The tenant is in the URL: the API answers 404 when this user belongs to another tenant.
    path: 'tenants/:tenantId/users/:id',
    title: 'titles.user',
    loadComponent: () => import('./users/user-detail').then((m) => m.UserDetail),
  },
  {
    path: 'users',
    title: 'titles.users',
    loadComponent: () => import('./users/users-page').then((m) => m.UsersPage),
  },
  {
    path: 'users/deleted',
    title: 'titles.deletedUsers',
    data: { deleted: true },
    loadComponent: () => import('./users/users-page').then((m) => m.UsersPage),
  },
];
