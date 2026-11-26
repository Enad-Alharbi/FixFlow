import { Component, inject, input } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { isValidEmail } from '../../../core/auth/account-rules';
import { coded, shownCode } from '../../../core/forms/coded';
import { ApiErrors } from '../../../core/http/api-error';
import { Field, FieldInput } from '../../../shared/ui/field/field';
import { TenantSettings } from './tenants.api';

/** Tenant settings (CLAUDE.md §9.5) with the API's rules and codes (Features/Tenants/TenantService.Validate). */
export function createTenantSettingsForm(value?: Partial<TenantSettings>) {
  const control = (initial: string, code?: string, ok?: (v: string) => boolean) =>
    new FormControl(initial, {
      nonNullable: true,
      validators: code && ok ? [coded(code, ok)] : [],
    });
  return new FormGroup({
    name: control(
      value?.name ?? '',
      'tenant.name_invalid',
      (v) => v.trim().length >= 2 && v.trim().length <= 200,
    ),
    defaultLanguage: control(value?.defaultLanguage ?? 'ar'),
    timeZone: control(
      value?.timeZone ?? 'Asia/Riyadh',
      'tenant.time_zone_invalid',
      (v) => v.length > 0,
    ),
    contactName: control(
      value?.contactName ?? '',
      'tenant.contact_name_invalid',
      (v) => v.trim().length <= 100,
    ),
    contactEmail: control(
      value?.contactEmail ?? '',
      'tenant.contact_email_invalid',
      (v) => !v.trim() || isValidEmail(v.trim()),
    ),
    contactPhone: control(value?.contactPhone ?? ''),
  });
}

export type TenantSettingsForm = ReturnType<typeof createTenantSettingsForm>;
type SettingsField = keyof TenantSettingsForm['controls'];

// IANA ids from the browser itself (the API accepts IANA ids only, e.g. "Asia/Riyadh").
const TIME_ZONES = Intl.supportedValuesOf('timeZone');

/** The settings fields shared by "New tenant" and the tenant's edit form. The parent owns form and submit. */
@Component({
  selector: 'app-tenant-settings-fields',
  imports: [ReactiveFormsModule, TranslocoDirective, Field, FieldInput],
  template: `
    <div class="fields" *transloco="let t" [formGroup]="form()">
      <app-field
        class="wide"
        [label]="t('platform.tenants.fields.name')"
        [error]="error('name', t)"
      >
        <input appInput formControlName="name" autocomplete="off" dir="auto" />
      </app-field>

      <app-field
        [label]="t('platform.tenants.fields.defaultLanguage')"
        [error]="error('defaultLanguage', t)"
      >
        <select appInput formControlName="defaultLanguage">
          <option value="ar">{{ t('language.names.ar') }}</option>
          <option value="en">{{ t('language.names.en') }}</option>
        </select>
      </app-field>

      <app-field [label]="t('platform.tenants.fields.timeZone')" [error]="error('timeZone', t)">
        <select appInput formControlName="timeZone" dir="ltr">
          @for (zone of timeZones; track zone) {
            <option [value]="zone">{{ zone }}</option>
          }
        </select>
      </app-field>

      <app-field
        [label]="t('platform.tenants.fields.contactName')"
        [error]="error('contactName', t)"
      >
        <input appInput formControlName="contactName" autocomplete="off" dir="auto" />
      </app-field>

      <app-field
        [label]="t('platform.tenants.fields.contactEmail')"
        [error]="error('contactEmail', t)"
      >
        <input appInput formControlName="contactEmail" type="email" autocomplete="off" dir="ltr" />
      </app-field>

      <app-field
        [label]="t('platform.tenants.fields.contactPhone')"
        [hint]="t('users.fields.phoneHint')"
        [error]="error('contactPhone', t)"
      >
        <input appInput formControlName="contactPhone" type="tel" autocomplete="off" dir="ltr" />
      </app-field>
    </div>
  `,
  styles: `
    @use 'styles/breakpoints' as bp;

    .fields {
      display: grid;
      gap: var(--space-5);

      @include bp.from(md) {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }

    .wide {
      grid-column: 1 / -1;
    }
  `,
})
export class TenantSettingsFields {
  private readonly apiErrors = inject(ApiErrors);

  readonly form = input.required<TenantSettingsForm>();
  readonly serverCodes = input<Record<string, string>>({});
  readonly submitted = input(false);

  protected readonly timeZones = TIME_ZONES;

  /** Translated error under a field, or null. `t` is the template's translate function. */
  protected error(field: SettingsField, t: (key: string) => string): string | null {
    const code = shownCode(
      this.form().controls[field],
      this.submitted(),
      this.serverCodes()[field],
    );
    return code ? t(this.apiErrors.codeKey(code)) : null;
  }
}
