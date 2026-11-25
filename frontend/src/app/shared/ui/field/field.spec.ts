import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTestTranslations } from '../../../core/i18n/testing';
import { PasswordChecklist } from '../password-checklist/password-checklist';
import { Field, FieldInput } from './field';

@Component({
  imports: [Field, FieldInput, PasswordChecklist],
  template: `
    <app-field label="Room number" [hint]="hint()" [error]="error()"><input appInput /></app-field>
    <app-field label="Other"><input appInput /></app-field>
    <app-password-checklist [password]="password()" />
  `,
})
class Host {
  readonly hint = signal<string | null>('As written on the door.');
  readonly error = signal<string | null>(null);
  readonly password = signal('');
}

describe('Field and PasswordChecklist', () => {
  async function render() {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideTestTranslations()] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('links the label, hint and error to the input, with unique ids per field', async () => {
    const { fixture, el } = await render();
    const [input, other] = Array.from(el.querySelectorAll('input'));

    expect(el.querySelector(`label[for="${input.id}"]`)?.textContent).toBe('Room number');
    expect(input.id).not.toBe(other.id);
    expect(document.getElementById(input.getAttribute('aria-describedby')!)?.textContent).toBe(
      'As written on the door.',
    );
    expect(input.hasAttribute('aria-invalid')).toBe(false);

    fixture.componentInstance.error.set('Enter a room number.');
    await fixture.whenStable();

    expect(input.getAttribute('aria-invalid')).toBe('true');
    const describedBy = input.getAttribute('aria-describedby')!.split(' ');
    expect(describedBy.map((id) => document.getElementById(id)?.textContent?.trim())).toEqual([
      'As written on the door.',
      'Enter a room number.',
    ]);
    expect(other.hasAttribute('aria-describedby')).toBe(false);
  });

  it('password checklist ticks rules as they are met and says so to screen readers', async () => {
    const { fixture, el } = await render();
    const items = () => Array.from(el.querySelectorAll('app-password-checklist li'));

    expect(items()).toHaveLength(6);
    expect(items().every((li) => !li.classList.contains('met'))).toBe(true);
    expect(items()[0].textContent).toContain('8–128 characters');

    fixture.componentInstance.password.set('Valid#Pass1');
    await fixture.whenStable();

    expect(items().every((li) => li.classList.contains('met'))).toBe(true);
    expect(items()[0].querySelector('.visually-hidden')?.textContent).toBe('met');
  });
});
