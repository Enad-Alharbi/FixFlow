import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  IconArchive,
  IconBan,
  IconCircleCheck,
  IconCircleX,
  IconInbox,
  IconTool,
  IconUserCheck,
  TablerIcon,
  TablerIconComponent,
} from '@tabler/icons-angular';

export type RequestStatus =
  'submitted' | 'assigned' | 'inProgress' | 'resolved' | 'closed' | 'rejected' | 'cancelled';

/** One icon per status, so colour is never the only signal (design-direction.md §5). */
const ICONS: Record<RequestStatus, TablerIcon> = {
  submitted: IconInbox,
  assigned: IconUserCheck,
  inProgress: IconTool,
  resolved: IconCircleCheck,
  closed: IconArchive,
  rejected: IconCircleX,
  cancelled: IconBan,
};

/** The status band from the Work tag: status colour + icon + localized label. */
@Component({
  selector: 'app-status-badge',
  imports: [TablerIconComponent, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <tabler-icon [icon]="icon()" aria-hidden="true" />{{ t('status.' + status()) }}
    </ng-container>
  `,
  styleUrl: './status-badge.scss',
  host: { '[attr.data-status]': 'status()' },
})
export class StatusBadge {
  readonly status = input.required<RequestStatus>();
  protected readonly icon = computed(() => ICONS[this.status()]);
}
