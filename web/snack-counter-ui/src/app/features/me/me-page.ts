import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiClient } from '../../core/api';
import { formatMoney, shortDate } from '../../core/format';
import { AvailabilityEntry, AvailabilityKind, ClaimSummary } from '../../core/models';
import { OverviewStore } from '../../core/overview.store';
import { SessionStore } from '../../core/session';
import { Toaster } from '../../core/toast';
import { chipClass, claimChip } from '../../shared/status';

const KIND_TEXT: Record<AvailabilityKind, string> = {
  Holiday: 'On holiday',
  Remote: 'Working from home',
  AtOffice: 'At another office',
};

@Component({
  selector: 'app-me-page',
  imports: [FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './me-page.html',
  styleUrl: './me-page.scss',
})
export class MePage {
  private readonly api = inject(ApiClient);
  protected readonly session = inject(SessionStore);
  protected readonly overview = inject(OverviewStore);
  private readonly toaster = inject(Toaster);

  protected readonly availability = signal<AvailabilityEntry[]>([]);
  protected readonly claims = signal<ClaimSummary[]>([]);

  protected kind: AvailabilityKind = 'Holiday';
  protected from = '';
  protected to = '';
  protected visitingOfficeId = '';

  protected readonly otherOffices = computed(() =>
    this.overview.offices().filter(o => o.id !== this.session.me()?.homeOfficeId));
  protected readonly unpaidCount = computed(() => this.claims().filter(c => c.status !== 'Paid').length);

  constructor() {
    if (!this.overview.loaded()) void this.overview.refresh();
    // Reload whenever the viewer switches person in the header.
    effect(() => {
      if (this.session.me()) void this.load();
    });
  }

  async load(): Promise<void> {
    try {
      const [availability, claims] = await Promise.all([this.api.availability(), this.api.claims()]);
      this.availability.set(availability);
      this.claims.set(claims);
    } catch { /* toast already shown */ }
  }

  protected async addAvailability(): Promise<void> {
    if (!this.from || !this.to) { this.toaster.bad('Pick a start and end date.'); return; }
    if (this.kind === 'AtOffice' && !this.visitingOfficeId) { this.toaster.bad('Pick the office you will be at.'); return; }
    try {
      await this.api.addAvailability({
        from: this.from, to: this.to, kind: this.kind,
        visitingOfficeId: this.kind === 'AtOffice' ? this.visitingOfficeId : null,
      });
      this.toaster.good('Saved. You won\'t be asked to refill while you\'re away.');
      this.from = this.to = '';
      await this.load();
    } catch { /* toast already shown */ }
  }

  protected async resubmit(claim: ClaimSummary, event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    try {
      await this.api.resubmit(claim.id, file);
      this.toaster.good('New receipt sent to HR.');
      await this.load();
    } catch { /* toast already shown */ }
  }

  protected officeName(id: string | null): string { return id ? this.overview.officeById().get(id)?.name ?? '' : ''; }
  protected kindText(entry: AvailabilityEntry): string {
    return entry.kind === 'AtOffice' ? `At the ${this.officeName(entry.officeId)} office` : KIND_TEXT[entry.kind];
  }
  protected kinds = Object.entries(KIND_TEXT) as [AvailabilityKind, string][];
  protected money = formatMoney;
  protected when = shortDate;
  protected claimChip = claimChip;
  protected chipClass = chipClass;
  protected days = (list: string[]) => list.map(d => d.slice(0, 3)).join(', ');
}
