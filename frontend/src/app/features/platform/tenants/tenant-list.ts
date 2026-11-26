import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  IconBuildingCommunity,
  IconPlus,
  IconSearch,
  TablerIconComponent,
} from '@tabler/icons-angular';
import { debounceTime } from 'rxjs';
import { ApiErrors } from '../../../core/http/api-error';
import { LocalDatePipe } from '../../../core/i18n/local-date.pipe';
import { Button } from '../../../shared/ui/button/button';
import { EmptyState } from '../../../shared/ui/empty-state/empty-state';
import { Field, FieldInput } from '../../../shared/ui/field/field';
import { Pagination } from '../../../shared/ui/pagination/pagination';
import { SortHeader, Table } from '../../../shared/ui/table/table';
import { TenantStatusBadge } from './tenant-status-badge';
import { Paged, TENANTS_URL, TenantListItem, TenantStatus } from './tenants.api';

/** '' = the default view (Active + Suspended; archived tenants are hidden, CLAUDE.md §9.4). */
type StatusFilter = '' | TenantStatus | 'all';
const STATUS_PARAM: Record<StatusFilter, TenantStatus[] | undefined> = {
  '': undefined,
  Active: ['Active'],
  Suspended: ['Suspended'],
  Archived: ['Archived'],
  all: ['Active', 'Suspended', 'Archived'],
};

/**
 * Control Panel: tenants. The filters live in the URL (?search=&status=&sort=&page=), so Back, reload and a
 * shared link all show the same list. The list itself is an httpResource: it re-fetches whenever they change.
 */
@Component({
  selector: 'app-tenant-list',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoDirective,
    TablerIconComponent,
    LocalDatePipe,
    Button,
    EmptyState,
    Field,
    FieldInput,
    Pagination,
    Table,
    SortHeader,
    TenantStatusBadge,
  ],
  templateUrl: './tenant-list.html',
  styleUrl: './tenant-list.scss',
})
export class TenantList {
  private readonly router = inject(Router);
  protected readonly apiErrors = inject(ApiErrors);

  // Query params, bound by the router (withComponentInputBinding).
  readonly search = input<string>();
  readonly status = input<string>();
  readonly sort = input<string>();
  readonly page = input<string>();

  protected readonly statusFilter = computed(() =>
    (this.status() ?? '') in STATUS_PARAM ? (this.status() as StatusFilter) : '',
  );
  protected readonly statusOptions = Object.keys(STATUS_PARAM) as StatusFilter[];

  protected readonly tenants = httpResource<Paged<TenantListItem>>(() => ({
    url: TENANTS_URL,
    params: {
      ...(this.search() ? { search: this.search()! } : {}),
      ...(STATUS_PARAM[this.statusFilter()] ? { status: STATUS_PARAM[this.statusFilter()]! } : {}),
      sort: this.sort() ?? 'name',
      page: this.page() ?? '1',
      pageSize: '20',
    },
  }));

  /** The search box updates the URL 300 ms after typing stops, not on every key. */
  protected readonly searchBox = new FormControl('', { nonNullable: true });
  protected readonly skeletonRows = [1, 2, 3, 4, 5];
  protected readonly icons = { add: IconPlus, search: IconSearch, empty: IconBuildingCommunity };
  protected readonly filtered = computed(() => !!this.search() || this.statusFilter() !== '');

  constructor() {
    effect(() => this.searchBox.setValue(this.search() ?? '', { emitEvent: false }));
    this.searchBox.valueChanges
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe((search) => this.navigate({ search: search.trim() || null, page: null }));
  }

  protected setStatus(status: string): void {
    this.navigate({ status: status || null, page: null });
  }

  protected setSort(sort: string): void {
    this.navigate({ sort, page: null });
  }

  protected setPage(page: number): void {
    this.navigate({ page: page > 1 ? page : null });
  }

  /** Merges into the current query string; null removes a parameter. */
  private navigate(queryParams: Record<string, string | number | null>): void {
    void this.router.navigate([], { queryParams, queryParamsHandling: 'merge' });
  }
}
