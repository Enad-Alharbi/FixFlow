import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { IconSearch, IconTrash, IconUsers } from '@tabler/icons-angular';
import { debounceTime } from 'rxjs';
import { ApiErrors } from '../../../core/http/api-error';
import { LocalDatePipe } from '../../../core/i18n/local-date.pipe';
import { Button } from '../../../shared/ui/button/button';
import { EmptyState } from '../../../shared/ui/empty-state/empty-state';
import { Field, FieldInput } from '../../../shared/ui/field/field';
import { Pagination } from '../../../shared/ui/pagination/pagination';
import { SortHeader, Table } from '../../../shared/ui/table/table';
import { TENANT_ROLES } from '../../users/user-fields/user-fields';
import { Paged } from '../tenants/tenants.api';
import { AccountStateBadge } from './account-state-badge';
import { PLATFORM_USERS_URL, UserListItem, tenantUsersUrl } from './users.api';

/**
 * Users with search, role filter, sorting and paging. One tenant's users (tenantId set) or every tenant's
 * (tenantId null: adds a Tenant column). deleted=true is the Deleted users view.
 * shortcut: filters live in the component, not the URL like the tenant list; move them to the URL if admins
 * need shareable or Back-button-safe user searches.
 */
@Component({
  selector: 'app-user-table',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoDirective,
    LocalDatePipe,
    Button,
    EmptyState,
    Field,
    FieldInput,
    Pagination,
    Table,
    SortHeader,
    AccountStateBadge,
  ],
  templateUrl: './user-table.html',
  styles: `
    @use 'styles/breakpoints' as bp;

    :host {
      display: grid;
      gap: var(--space-4);
    }

    .filters {
      display: grid;
      gap: var(--space-4);

      @include bp.from(md) {
        grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
      }
    }

    .row-link {
      color: var(--color-accent);
      font-weight: var(--fw-semibold);
      text-decoration: none;

      &:hover {
        text-decoration: underline;
      }
    }

    .muted {
      color: var(--color-text-muted);
    }
  `,
})
export class UserTable {
  protected readonly apiErrors = inject(ApiErrors);

  readonly tenantId = input<string | null>(null);
  readonly deleted = input(false);

  protected readonly search = signal('');
  protected readonly role = signal('');
  protected readonly sort = signal('displayName');
  protected readonly page = signal(1);

  protected readonly users = httpResource<Paged<UserListItem>>(() => ({
    url: this.tenantId() ? tenantUsersUrl(this.tenantId()!) : PLATFORM_USERS_URL,
    params: {
      ...(this.search() ? { search: this.search() } : {}),
      ...(this.role() ? { role: this.role() } : {}),
      deleted: this.deleted(),
      sort: this.sort(),
      page: this.page(),
      pageSize: 20,
    },
  }));

  protected readonly roles = TENANT_ROLES;
  protected readonly global = computed(() => !this.tenantId());
  protected readonly filtered = computed(() => !!this.search() || !!this.role());
  protected readonly skeletonCells = computed(() => [...Array(this.global() ? 6 : 5).keys()]);
  protected readonly skeletonRows = [1, 2, 3, 4, 5];
  protected readonly searchBox = new FormControl('', { nonNullable: true });
  protected readonly icons = { search: IconSearch, users: IconUsers, deleted: IconTrash };

  constructor() {
    this.searchBox.valueChanges.pipe(debounceTime(300), takeUntilDestroyed()).subscribe((value) => {
      this.search.set(value.trim());
      this.page.set(1);
    });
  }

  /** For the parent, e.g. after creating a user. */
  reload(): void {
    this.users.reload();
  }

  protected setRole(role: string): void {
    this.role.set(role);
    this.page.set(1);
  }

  protected setSort(sort: string): void {
    this.sort.set(sort);
    this.page.set(1);
  }
}
