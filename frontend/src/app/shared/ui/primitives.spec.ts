import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LanguageService } from '../../core/i18n/language.service';
import { provideTestTranslations } from '../../core/i18n/testing';
import { Button } from './button/button';
import { Priority, PriorityIndicator } from './priority/priority';
import { RequestStatus, StatusBadge } from './status-badge/status-badge';

@Component({
  imports: [Button, StatusBadge, PriorityIndicator],
  template: `
    <button appButton variant="primary" type="button" [loading]="loading()">Save changes</button>
    <app-status-badge [status]="status()" />
    <app-priority [priority]="priority()" />
  `,
})
class Host {
  readonly loading = signal(false);
  readonly status = signal<RequestStatus>('assigned');
  readonly priority = signal<Priority>('high');
}

describe('UI primitives', () => {
  async function render() {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideTestTranslations()] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('button: loading announces busy state but keeps the button focusable (not natively disabled)', async () => {
    const { fixture, el } = await render();
    const button = el.querySelector('button')!;
    expect(button.classList).toContain('btn-primary');
    expect(button.getAttribute('aria-busy')).toBeNull();

    fixture.componentInstance.loading.set(true);
    await fixture.whenStable();

    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.disabled).toBe(false);
    expect(button.textContent).toContain('Loading…');
  });

  it('status badge: icon + localized label, colour picked by data-status', async () => {
    const { fixture, el } = await render();
    const badge = el.querySelector('app-status-badge')!;
    expect(badge.getAttribute('data-status')).toBe('assigned');
    expect(badge.querySelector('tabler-icon')).not.toBeNull();
    expect(badge.textContent?.trim()).toBe('Assigned');

    fixture.componentInstance.status.set('inProgress');
    await TestBed.inject(LanguageService).use('ar');
    await fixture.whenStable();

    expect(badge.getAttribute('data-status')).toBe('inProgress');
    expect(badge.textContent?.trim()).toBe('قيد التنفيذ');
  });

  it('priority: label and value text, never colour alone', async () => {
    const { el } = await render();
    const priority = el.querySelector('app-priority')!;
    expect(priority.getAttribute('data-priority')).toBe('high');
    expect(priority.textContent).toContain('Priority');
    expect(priority.textContent).toContain('High');
  });
});
