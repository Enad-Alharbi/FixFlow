import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { TenantRole } from '../../users/user-fields/user-fields';
import { NewUser, TENANTS_URL } from '../tenants/tenants.api';

/** Mirrors Features/Users/UserContracts.cs. */
export interface UserDetails {
  id: string;
  tenantId: string;
  userName: string;
  email: string;
  displayName: string;
  phone: string | null;
  role: TenantRole;
  isActive: boolean;
  isDeleted: boolean;
  deletedAt: string | null;
  /** In the future = locked until then. */
  lockoutEnd: string | null;
  lastSignInAt: string | null;
}

export interface UserListItem extends Omit<UserDetails, 'phone'> {
  tenantName: string;
  tenantCode: string;
}

export type UserProfile = Pick<UserDetails, 'displayName' | 'userName' | 'email'> & {
  phone: string;
};

/** One state per account, in order of what matters most: deleted, then deactivated, then locked. */
export type AccountState = 'deleted' | 'deactivated' | 'locked' | 'active';
export function accountState(
  user: Pick<UserDetails, 'isDeleted' | 'isActive' | 'lockoutEnd'>,
): AccountState {
  if (user.isDeleted) return 'deleted';
  if (!user.isActive) return 'deactivated';
  if (user.lockoutEnd && new Date(user.lockoutEnd) > new Date()) return 'locked';
  return 'active';
}

export const PLATFORM_USERS_URL = `${environment.apiUrl}/api/platform/users`;
export const tenantUsersUrl = (tenantId: string) => `${TENANTS_URL}/${tenantId}/users`;
export const userUrl = (tenantId: string, id: string) => `${tenantUsersUrl(tenantId)}/${id}`;

type UserAction = 'deactivate' | 'reactivate' | 'unlock' | 'restore';

/** Writes. Every call names the tenant: the API answers 404 for a user of any other tenant. */
@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  create(tenantId: string, user: NewUser): Promise<UserDetails> {
    return firstValueFrom(this.http.post<UserDetails>(tenantUsersUrl(tenantId), user));
  }

  update(tenantId: string, id: string, profile: UserProfile): Promise<UserDetails> {
    return firstValueFrom(this.http.put<UserDetails>(userUrl(tenantId, id), profile));
  }

  changeRole(tenantId: string, id: string, role: TenantRole): Promise<UserDetails> {
    return firstValueFrom(this.http.post<UserDetails>(`${userUrl(tenantId, id)}/role`, { role }));
  }

  setPassword(tenantId: string, id: string, password: string): Promise<UserDetails> {
    return firstValueFrom(
      this.http.post<UserDetails>(`${userUrl(tenantId, id)}/password`, { password }),
    );
  }

  act(tenantId: string, id: string, action: UserAction): Promise<UserDetails> {
    return firstValueFrom(this.http.post<UserDetails>(`${userUrl(tenantId, id)}/${action}`, null));
  }

  /** Soft delete. */
  delete(tenantId: string, id: string): Promise<UserDetails> {
    return firstValueFrom(this.http.delete<UserDetails>(userUrl(tenantId, id)));
  }
}
