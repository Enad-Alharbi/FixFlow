import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideTestTranslations } from '../../core/i18n/testing';
import { Shell } from './shell';

@Component({ template: '<p>page</p>' })
class Page {}

describe('Shell', () => {
  async function open(url: string) {
    TestBed.configureTestingModule({
      providers: [
        provideTestTranslations(),
        provideRouter(
          [
            {
              path: 'platform',
              component: Shell,
              data: { plane: 'platform' },
              children: [{ path: '', component: Page }],
            },
            {
              path: '',
              component: Shell,
              data: { plane: 'tenant' },
              children: [{ path: '', component: Page }],
            },
          ],
          withComponentInputBinding(),
        ),
      ],
    });
    const harness = await RouterTestingHarness.create(url);
    return harness.routeNativeElement as HTMLElement;
  }

  it('marks the Control Panel with the Platform tag and the platform style', async () => {
    await open('/platform');
    const shell = document.querySelector('app-shell')!;

    expect(shell.classList).toContain('platform');
    expect(shell.querySelector('.plane-tag')?.textContent?.trim()).toBe('Platform');
  });

  it('shows no Platform tag in the tenant app', async () => {
    await open('/');
    const shell = document.querySelector('app-shell')!;

    expect(shell.classList).not.toContain('platform');
    expect(shell.querySelector('.plane-tag')).toBeNull();
  });

  it('marks the current page in the navigation for screen readers', async () => {
    await open('/');
    const link = document.querySelector('app-shell .nav-link')!;

    expect(link.getAttribute('aria-current')).toBe('page');
    expect(link.textContent?.trim()).toBe('System status');
  });

  it('skip link moves focus to the main content', async () => {
    await open('/');
    (document.querySelector('app-shell .skip-link') as HTMLAnchorElement).click();

    expect(document.activeElement?.id).toBe('main');
  });
});
