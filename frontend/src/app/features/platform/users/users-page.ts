import { Component, booleanAttribute, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { UserTable } from './user-table';

/**
 * Control Panel: users across every tenant, and the Deleted users view (CLAUDE.md §10.1). New users are created
 * from their tenant's page, where the tenant is known.
 */
@Component({
  selector: 'app-users-page',
  imports: [RouterLink, RouterLinkActive, TranslocoDirective, UserTable],
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('platform.users.title') }}</h1>
      <nav class="views" [attr.aria-label]="t('platform.users.views')">
        <a
          routerLink="/platform/users"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
          [routerLinkActiveOptions]="{ exact: true }"
        >
          {{ t('platform.users.allUsers') }}
        </a>
        <a
          routerLink="/platform/users/deleted"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
        >
          {{ t('platform.users.deletedUsers') }}
        </a>
      </nav>
      @if (deleted()) {
        <p class="note">{{ t('platform.users.deletedNote') }}</p>
      }
      <app-user-table [deleted]="deleted()" />
    </ng-container>
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-5);
    }

    .views {
      display: flex;
      gap: var(--space-5);
      border-block-end: var(--border-width) solid var(--color-border);
    }

    .views a {
      padding-block: var(--space-2);
      border-block-end: var(--space-1) solid transparent;
      color: var(--color-text-muted);
      font-family: var(--font-display);
      font-weight: var(--fw-semibold);
      text-decoration: none;
    }

    .views a.active {
      border-block-end-color: var(--color-accent);
      color: var(--color-text);
    }

    .note {
      margin: 0;
      color: var(--color-text-muted);
    }
  `,
})
export class UsersPage {
  /**
   * From route data: the Deleted users view. booleanAttribute, because router input binding sets an input to
   * undefined (not its default) when the route has no such data, and the API rejects deleted=undefined.
   */
  readonly deleted = input(false, { transform: booleanAttribute });
}
