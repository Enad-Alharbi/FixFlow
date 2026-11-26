import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTestTranslations } from '../../../core/i18n/testing';
import { Confirm } from '../../../shared/ui/dialog/dialog';
import { UserDetail } from './user-detail';
import { UserDetails, accountState, userUrl } from './users.api';
import { TENANTS_URL } from '../tenants/tenants.api';

const base: UserDetails = {
  id: 'u1',
  tenantId: 't1',
  userName: 'sara',
  email: 'sara@example.com',
  displayName: 'Sara',
  phone: null,
  role: 'Supervisor',
  isActive: true,
  isDeleted: false,
  deletedAt: null,
  lockoutEnd: null,
  lastSignInAt: null,
};

describe('accountState', () => {
  it('reports the state that matters most', () => {
    const future = new Date(Date.now() + 60_000).toISOString();
    const past = new Date(Date.now() - 60_000).toISOString();
    expect(accountState({ ...base, isDeleted: true, isActive: false, lockoutEnd: future })).toBe(
      'deleted',
    );
    expect(accountState({ ...base, isActive: false, lockoutEnd: future })).toBe('deactivated');
    expect(accountState({ ...base, lockoutEnd: future })).toBe('locked');
    expect(accountState({ ...base, lockoutEnd: past })).toBe('active'); // an expired lock is no lock
  });
});

describe('UserDetail', () => {
  async function render(user: UserDetails) {
    TestBed.configureTestingModule({
      imports: [UserDetail],
      providers: [
        provideTestTranslations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Confirm, useValue: { ask: () => Promise.resolve(true) } },
      ],
    });
    const fixture = TestBed.createComponent(UserDetail);
    fixture.componentRef.setInput('tenantId', 't1');
    fixture.componentRef.setInput('id', 'u1');
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(userUrl('t1', 'u1')).flush(user);
    http.expectOne(`${TENANTS_URL}/t1`).flush({ id: 't1', name: 'Acme' });
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const button = (label: string) =>
      [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === label);
    return { fixture, el, http, button };
  }

  it('offers only Restore for a deleted user and keeps the profile read-only', async () => {
    const { el, button } = await render({
      ...base,
      isDeleted: true,
      deletedAt: '2026-11-25T10:00:00Z',
    });

    expect(button('Restore')).toBeDefined();
    expect(button('Deactivate')).toBeUndefined();
    expect(button('Delete')).toBeUndefined();
    expect([...el.querySelectorAll('input')].every((i) => i.disabled)).toBe(true);
  });

  it("shows the API's last-supervisor refusal and leaves the user as they were", async () => {
    const { fixture, el, http, button } = await render(base);

    button('Deactivate')!.click();
    await fixture.whenStable();
    http
      .expectOne(`${userUrl('t1', 'u1')}/deactivate`)
      .flush({ code: 'user.last_supervisor' }, { status: 409, statusText: 'Conflict' });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(el.querySelector('[role=alert]')?.textContent).toContain(
        "This is the organization's only active supervisor.",
      );
    });
    expect(el.textContent).toContain('Active');
  });
});
