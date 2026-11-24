import { Component, computed, linkedSignal } from '@angular/core';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { TranslocoDirective } from '@jsverse/transloco';
import { environment } from '../../../environments/environment';
import { IconRefresh, TablerIconComponent } from '@tabler/icons-angular';
import { LocalDatePipe } from '../../core/i18n/local-date.pipe';
import { Button } from '../../shared/ui/button/button';

@Component({
  selector: 'app-system-status',
  imports: [TranslocoDirective, LocalDatePipe, Button, TablerIconComponent],
  templateUrl: './system-status.html',
  styles: `
    section {
      max-inline-size: 40rem;
      display: grid;
      gap: var(--space-4);
    }

    .checked {
      color: var(--color-text-muted);
      font-size: var(--fs-sm);
    }

    [appButton] {
      justify-self: start;
    }
  `,
})
export class SystemStatus {
  protected readonly refreshIcon = IconRefresh;

  // Calls GET /health/ready, which reports "Healthy" only when the API can also reach SQL Server.
  protected readonly health = httpResource.text(() => `${environment.apiUrl}/health/ready`);

  /** HTTP status of a failed check (e.g. 503), or null when the API could not be reached at all. */
  protected readonly errorStatus = computed(() => {
    const error = this.health.error();
    return error instanceof HttpErrorResponse && error.status > 0 ? error.status : null;
  });

  /** When the last check finished (success or failure); kept while a re-check is loading. */
  protected readonly checkedAt = linkedSignal<boolean, Date | null>({
    source: () => this.health.isLoading(),
    computation: (loading, previous) => (loading ? (previous?.value ?? null) : new Date()),
  });
}
