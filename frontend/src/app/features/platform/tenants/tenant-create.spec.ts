import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideTestTranslations } from '../../../core/i18n/testing';
import { TenantCreate } from './tenant-create';
import { TenantsApi } from './tenants.api';

describe('TenantCreate', () => {
  let api: { create: ReturnType<typeof vi.fn> };
  beforeEach(() => (api = { create: vi.fn() }));

  async function render() {
    TestBed.configureTestingModule({
      imports: [TenantCreate],
      providers: [
        provideTestTranslations(),
        provideRouter([]),
        { provide: TenantsApi, useValue: api },
      ],
    });
    const fixture = TestBed.createComponent(TenantCreate);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const field = (label: string) => {
      const forId = [...el.querySelectorAll('label')].find(
        (l) => l.textContent?.trim() === label,
      )!.htmlFor;
      return el.querySelector<HTMLInputElement>(`#${forId}`)!;
    };
    const type = (label: string, value: string) => {
      const input = field(label);
      input.value = value;
      input.dispatchEvent(new Event('input'));
    };
    const submit = async () => {
      el.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
    };
    const fillValid = () => {
      type('Code', 'riyadh-hospital');
      type('Name', 'Riyadh Hospital');
      type('Display name', 'Sara');
      type('Username', 'rh.sara');
      type('Email', 'sara@example.com');
      type('Password', 'Valid#Pass1');
    };
    return { fixture, el, field, type, submit, fillValid, navigate };
  }

  it('checks the fields with the API rules before sending anything', async () => {
    const { el, submit } = await render();

    await submit();

    expect(api.create).not.toHaveBeenCalled();
    expect(el.textContent).toContain(
      'Use 2–30 lowercase letters, numbers or -, starting with a letter.',
    );
    expect(el.textContent).toContain("The password doesn't meet all the rules below.");
  });

  it('sends the tenant with its first supervisor, always as a Supervisor, then opens the new tenant', async () => {
    api.create.mockResolvedValue({ id: 't1', name: 'Riyadh Hospital' });
    const { submit, fillValid, navigate } = await render();

    fillValid();
    await submit();

    expect(api.create).toHaveBeenCalledWith(
      expect.objectContaining({
        code: 'riyadh-hospital',
        name: 'Riyadh Hospital',
        timeZone: 'Asia/Riyadh',
        firstSupervisor: expect.objectContaining({ userName: 'rh.sara', role: 'Supervisor' }),
      }),
    );
    expect(navigate).toHaveBeenCalledWith(['/platform/tenants', 't1']);
  });

  it("shows the supervisor's server errors under the supervisor's own fields", async () => {
    api.create.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: {
          code: 'validation.failed',
          errors: { 'firstSupervisor.email': ['user.email_taken'] },
        },
      }),
    );
    const { field, submit, fillValid } = await render();

    fillValid();
    await submit();

    const email = field('Email');
    expect(email.getAttribute('aria-invalid')).toBe('true');
    expect(
      document.getElementById(email.getAttribute('aria-describedby')!.split(' ').pop()!)
        ?.textContent,
    ).toContain('This email is already in use.');
  });
});
