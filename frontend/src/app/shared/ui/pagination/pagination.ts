import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { IconChevronLeft, IconChevronRight, TablerIconComponent } from '@tabler/icons-angular';
import { Button } from '../button/button';

/**
 * "Showing 21–40 of 137" with previous/next. Matches the API's paging envelope (page is 1-based).
 *   <app-pagination [page]="r.page" [pageSize]="r.pageSize" [total]="r.totalCount" (pageChange)="go($event)" />
 */
@Component({
  selector: 'app-pagination',
  imports: [TranslocoDirective, TablerIconComponent, Button],
  template: `
    <nav *transloco="let t; prefix: 'pagination'" [attr.aria-label]="t('label')">
      <p class="range">{{ t('range', { from: from(), to: to(), total: total() }) }}</p>
      <div class="buttons">
        <button
          appButton
          variant="ghost"
          type="button"
          [disabled]="page() <= 1"
          (click)="pageChange.emit(page() - 1)"
        >
          <tabler-icon class="icon-mirror" [icon]="icons.previous" aria-hidden="true" />{{
            t('previous')
          }}
        </button>
        <button
          appButton
          variant="ghost"
          type="button"
          [disabled]="page() >= pages()"
          (click)="pageChange.emit(page() + 1)"
        >
          {{ t('next') }}<tabler-icon class="icon-mirror" [icon]="icons.next" aria-hidden="true" />
        </button>
      </div>
    </nav>
  `,
  styles: `
    nav {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2) var(--space-4);
    }

    .range {
      margin: 0;
      color: var(--color-text-muted);
      font-variant-numeric: tabular-nums;
    }

    .buttons {
      display: flex;
      gap: var(--space-2);
    }
  `,
})
export class Pagination {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly pageChange = output<number>();

  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  protected readonly from = computed(() =>
    this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1,
  );
  protected readonly to = computed(() => Math.min(this.page() * this.pageSize(), this.total()));
  protected readonly icons = { previous: IconChevronLeft, next: IconChevronRight };
}
