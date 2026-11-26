import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { TenantRole } from '../../users/user-fields/user-fields';

/** Mirrors Features/Tenants/TenantContracts.cs. */
export type TenantStatus = 'Active' | 'Suspended' | 'Archived';
export type TenantAction = 'suspend' | 'reactivate' | 'archive' | 'unarchive';

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface TenantListItem {
  id: string;
  name: string;
  code: string;
  status: TenantStatus;
  defaultLanguage: 'en' | 'ar';
  timeZone: string;
  userCount: number;
  createdAt: string;
}

export interface TenantDetails extends TenantListItem {
  contactName: string | null;
  contactEmail: string | null;
  contactPhone: string | null;
  updatedAt: string;
  /** Send back on edit; a stale value gets 409 concurrency.conflict. */
  rowVersion: string;
}

export interface TenantSettings {
  name: string;
  defaultLanguage: string;
  timeZone: string;
  contactName: string;
  contactEmail: string;
  contactPhone: string;
}

export interface NewUser {
  displayName: string;
  userName: string;
  email: string;
  phone: string;
  role: TenantRole;
  password: string;
}

export const TENANTS_URL = `${environment.apiUrl}/api/platform/tenants`;

/** Writes. Reads use httpResource in the pages, so loading and error states come with the signal. */
@Injectable({ providedIn: 'root' })
export class TenantsApi {
  private readonly http = inject(HttpClient);

  create(
    settings: TenantSettings & { code: string; firstSupervisor: NewUser },
  ): Promise<TenantDetails> {
    return firstValueFrom(this.http.post<TenantDetails>(TENANTS_URL, settings));
  }

  update(id: string, settings: TenantSettings, rowVersion: string): Promise<TenantDetails> {
    return firstValueFrom(
      this.http.put<TenantDetails>(`${TENANTS_URL}/${id}`, { ...settings, rowVersion }),
    );
  }

  /** Unarchive needs the status to return to (Active or Suspended). */
  changeStatus(id: string, action: TenantAction, status?: TenantStatus): Promise<TenantDetails> {
    return firstValueFrom(
      this.http.post<TenantDetails>(`${TENANTS_URL}/${id}/${action}`, { status }),
    );
  }
}
