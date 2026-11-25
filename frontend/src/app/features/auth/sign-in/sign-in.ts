import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { IconAlertTriangle, IconEye, IconEyeOff, TablerIconComponent } from '@tabler/icons-angular';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiErrors } from '../../../core/http/api-error';
import { LanguageService } from '../../../core/i18n/language.service';
import { LocalDatePipe } from '../../../core/i18n/local-date.pipe';
import { Button } from '../../../shared/ui/button/button';
import { Field, FieldInput } from '../../../shared/ui/field/field';

/** Sign in with email or username + password (CLAUDE.md §7.4). One generic message for wrong credentials. */
@Component({
  selector: 'app-sign-in',
  imports: [
    ReactiveFormsModule,
    TranslocoDirective,
    TablerIconComponent,
    LocalDatePipe,
    Button,
    Field,
    FieldInput,
  ],
  templateUrl: './sign-in.html',
  styleUrl: './sign-in.scss',
})
export class SignIn {
  private readonly auth = inject(AuthService);
  private readonly language = inject(LanguageService);
  private readonly router = inject(Router);
  private readonly apiErrors = inject(ApiErrors);

  /** Where to go after signing in (set by the guards from the page the user tried to open). */
  readonly returnUrl = input<string>();

  protected readonly form = new FormGroup({
    login: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  protected readonly submitted = signal(false);
  protected readonly loading = signal(false);
  protected readonly showPassword = signal(false);
  private readonly error = signal<unknown>(null);

  protected readonly errorKey = computed(() =>
    this.error() ? this.apiErrors.key(this.error()) : null,
  );
  /** For the locked-account message: when the lock ends. */
  protected readonly lockoutEnd = computed(() => {
    const error = this.error();
    return error instanceof HttpErrorResponse
      ? ((error.error as { lockoutEnd?: string } | null)?.lockoutEnd ?? null)
      : null;
  });

  protected readonly icons = { show: IconEye, hide: IconEyeOff, alert: IconAlertTriangle };

  protected showError(control: 'login' | 'password'): boolean {
    const c = this.form.controls[control];
    return c.invalid && (c.touched || this.submitted());
  }

  protected async submit(): Promise<void> {
    this.submitted.set(true);
    this.error.set(null);
    if (this.form.invalid || this.loading()) return;

    this.loading.set(true);
    try {
      const { login, password } = this.form.getRawValue();
      const user = await this.auth.signIn(login, password);
      // Language order: saved choice -> tenant default -> browser -> English.
      await this.language.init(user.tenant?.defaultLanguage);
      await this.router.navigateByUrl(this.safeReturnUrl() ?? this.auth.homeUrl());
    } catch (error) {
      this.error.set(error);
      // Never leave a rejected password in the field. reset() also clears "touched", and un-submitting stops
      // the now-empty field from adding "Enter your password." under the server's message.
      this.form.controls.password.reset();
      this.submitted.set(false);
    } finally {
      this.loading.set(false);
    }
  }

  /** Only paths inside this app: "//evil.example" or "https://…" would turn sign-in into an open redirect. */
  private safeReturnUrl(): string | null {
    const url = this.returnUrl();
    return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/sign-in')
      ? url
      : null;
  }
}
