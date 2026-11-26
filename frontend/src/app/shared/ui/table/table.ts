import { Component, ViewEncapsulation, computed, input, output } from '@angular/core';
import {
  IconArrowDown,
  IconArrowUp,
  IconArrowsSort,
  TablerIconComponent,
} from '@tabler/icons-angular';

/**
 * Data table: a native <table> (screen readers get rows, columns and headers for free) with FixFlow's look.
 * Wrap it in <div class="table-scroll"> so a wide table scrolls inside its box on phones, not the whole page.
 *   <div class="table-scroll"><table appTable> <thead>…</thead> <tbody>…</tbody> </table></div>
 * Loading rows: <td><span class="skeleton"></span></td>.
 */
@Component({
  // eslint-disable-next-line @angular-eslint/component-selector -- attribute selector keeps the native <table>
  selector: 'table[appTable]',
  template: '<ng-content />',
  styleUrl: './table.scss',
  // Unscoped so the styles reach the projected rows and cells; every selector is prefixed with .data-table.
  encapsulation: ViewEncapsulation.None,
  host: { class: 'data-table' },
})
export class Table {}

/**
 * A sortable column header. Sort values follow the API: "name" ascending, "-name" descending.
 *   <th appSortHeader="name" [sort]="sort()" (sortChange)="setSort($event)">Name</th>
 * aria-sort tells screen readers the current order; the button announces the column name.
 */
@Component({
  // eslint-disable-next-line @angular-eslint/component-selector -- attribute selector keeps the native <th>
  selector: 'th[appSortHeader]',
  imports: [TablerIconComponent],
  template: `
    <button type="button" class="sort-button" (click)="toggle()">
      <ng-content />
      <tabler-icon [icon]="icon()" aria-hidden="true" />
    </button>
  `,
  host: { '[attr.aria-sort]': 'ariaSort()' },
})
export class SortHeader {
  readonly key = input.required<string>({ alias: 'appSortHeader' });
  readonly sort = input<string | null | undefined>(null);
  readonly sortChange = output<string>();

  private readonly direction = computed(() =>
    this.sort() === this.key() ? 'asc' : this.sort() === `-${this.key()}` ? 'desc' : null,
  );
  protected readonly ariaSort = computed(() =>
    this.direction() === 'asc' ? 'ascending' : this.direction() === 'desc' ? 'descending' : 'none',
  );
  protected readonly icon = computed(() =>
    this.direction() === 'asc'
      ? IconArrowUp
      : this.direction() === 'desc'
        ? IconArrowDown
        : IconArrowsSort,
  );

  /** First click sorts ascending; clicking the sorted column flips it. */
  protected toggle(): void {
    this.sortChange.emit(this.direction() === 'asc' ? `-${this.key()}` : this.key());
  }
}
