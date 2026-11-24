import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SystemStatus } from './system-status';
import { environment } from '../../../environments/environment';

describe('SystemStatus', () => {
  const url = `${environment.apiUrl}/health/ready`;

  async function render() {
    TestBed.configureTestingModule({
      imports: [SystemStatus],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(SystemStatus);
    // Runs effects so the resource sends its request. (whenStable would wait forever for the unanswered request.)
    TestBed.tick();
    return { fixture, http: TestBed.inject(HttpTestingController) };
  }

  async function statusText(fixture: Awaited<ReturnType<typeof render>>['fixture']) {
    await fixture.whenStable();
    return (fixture.nativeElement as HTMLElement)
      .querySelector('[role="status"]')
      ?.textContent?.trim();
  }

  it('shows Healthy when the API and database are ready', async () => {
    const { fixture, http } = await render();
    http.expectOne(url).flush('Healthy');
    expect(await statusText(fixture)).toBe('API and database: Healthy');
  });

  it('shows the HTTP status when the API answers but is not ready', async () => {
    const { fixture, http } = await render();
    http.expectOne(url).flush('Unhealthy', { status: 503, statusText: 'Service Unavailable' });
    expect(await statusText(fixture)).toBe('API answered but is not ready (HTTP 503).');
  });

  it('shows unreachable when there is no response at all', async () => {
    const { fixture, http } = await render();
    http.expectOne(url).error(new ProgressEvent('error'));
    expect(await statusText(fixture)).toBe('API is unreachable.');
  });
});
