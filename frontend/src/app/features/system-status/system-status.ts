import { Component, computed, linkedSignal } from '@angular/core';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { TranslocoDirective } from '@jsverse/transloco';
import { environment } from '../../../environments/environment';
import { LocalDatePipe } from '../../core/i18n/local-date.pipe';

@Component({
  selector: 'app-system-status',
  imports: [TranslocoDirective, LocalDatePipe],
  templateUrl: './system-status.html',
  styles: `
    main {
      max-inline-size: 40rem;
      margin-inline: auto;
      padding: var(--space-6) var(--space-4);
      display: grid;
      gap: var(--space-4);
    }

    .checked {
      color: var(--color-text-muted);
      font-size: var(--fs-sm);
    }

    button {
      justify-self: start;
    }
  `,
})
export class SystemStatus {
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
