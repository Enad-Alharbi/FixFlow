import { Component, computed } from '@angular/core';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { environment } from '../../../environments/environment';

// shortcut: English-only text, because the localization library arrives in Phase 2; this page is replaced or localized then.
@Component({
  selector: 'app-system-status',
  templateUrl: './system-status.html',
})
export class SystemStatus {
  // Calls GET /health/ready, which reports "Healthy" only when the API can also reach SQL Server.
  protected readonly health = httpResource.text(() => `${environment.apiUrl}/health/ready`);

  /** HTTP status of a failed check (e.g. 503), or null when the API could not be reached at all. */
  protected readonly errorStatus = computed(() => {
    const error = this.health.error();
    return error instanceof HttpErrorResponse && error.status > 0 ? error.status : null;
  });
}
