import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTestTranslations } from '../../../core/i18n/testing';
import { Pagination } from '../pagination/pagination';
import { SortHeader, Table } from './table';

@Component({
  imports: [Table, SortHeader, Pagination],
  template: `
    <table appTable>
      <thead>
        <tr>
          <th appSortHeader="name" [sort]="sort()" (sortChange)="sort.set($event)">Name</th>
        </tr>
      </thead>
    </table>
    <app-pagination [page]="page()" [pageSize]="20" [total]="45" (pageChange)="page.set($event)" />
  `,
})
class Host {
  readonly sort = signal('code');
  readonly page = signal(1);
}

describe('SortHeader and Pagination', () => {
  async function render() {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideTestTranslations()] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement, host: fixture.componentInstance };
  }

  it('sorts ascending first, flips on the next click, and tells screen readers the order', async () => {
    const { fixture, el, host } = await render();
    const th = el.querySelector('th')!;
    expect(th.getAttribute('aria-sort')).toBe('none');

    th.querySelector('button')!.click();
    await fixture.whenStable();
    expect(host.sort()).toBe('name');
    expect(th.getAttribute('aria-sort')).toBe('ascending');

    th.querySelector('button')!.click();
    await fixture.whenStable();
    expect(host.sort()).toBe('-name');
    expect(th.getAttribute('aria-sort')).toBe('descending');
  });

  it('shows the visible range and disables what would leave the pages', async () => {
    const { fixture, el, host } = await render();
    const [previous, next] = el.querySelectorAll<HTMLButtonElement>('app-pagination button');
    expect(el.textContent).toContain('Showing 1–20 of 45');
    expect(previous.disabled).toBe(true);

    host.page.set(3);
    await fixture.whenStable();
    expect(el.textContent).toContain('Showing 41–45 of 45');
    expect(next.disabled).toBe(true);
  });
});
