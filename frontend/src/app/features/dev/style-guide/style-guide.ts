import { Component, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  IconArrowLeft,
  IconArrowRight,
  IconCheck,
  IconSearch,
  IconTool,
  TablerIconComponent,
} from '@tabler/icons-angular';
import { Button, ButtonVariant } from '../../../shared/ui/button/button';
import { Field, FieldInput } from '../../../shared/ui/field/field';
import { PasswordChecklist } from '../../../shared/ui/password-checklist/password-checklist';
import { Priority, PriorityIndicator } from '../../../shared/ui/priority/priority';
import { RequestStatus, StatusBadge } from '../../../shared/ui/status-badge/status-badge';

/**
 * Development-only page (/dev/style-guide) that renders every token and primitive.
 * Every new primitive is added here (CLAUDE.md §15.2). Not routed in production builds (see app.routes.ts).
 */
@Component({
  selector: 'app-style-guide',
  imports: [
    TranslocoDirective,
    TablerIconComponent,
    Button,
    StatusBadge,
    PriorityIndicator,
    Field,
    FieldInput,
    PasswordChecklist,
  ],
  templateUrl: './style-guide.html',
  styleUrl: './style-guide.scss',
})
export class StyleGuide {
  protected readonly colors = [
    'color-page',
    'color-surface',
    'color-text',
    'color-text-muted',
    'color-border',
    'color-accent',
    'color-highlight',
    'color-danger',
    'color-chrome-platform',
    'color-platform-tag',
  ];
  protected readonly statuses: RequestStatus[] = [
    'submitted',
    'assigned',
    'inProgress',
    'resolved',
    'closed',
    'rejected',
    'cancelled',
  ];
  protected readonly priorities: Priority[] = ['low', 'medium', 'high', 'critical'];
  protected readonly typeScale = ['4xl', '3xl', '2xl', 'xl', 'lg', 'md', 'sm', 'xs'];
  protected readonly spacing = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
  protected readonly variants: ButtonVariant[] = ['primary', 'secondary', 'ghost'];
  protected readonly samplePassword = signal('');
  protected readonly icons = {
    back: IconArrowLeft,
    next: IconArrowRight,
    tool: IconTool,
    search: IconSearch,
    check: IconCheck,
  };
}
