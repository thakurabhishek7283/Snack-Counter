import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiClient } from '../../core/api';
import { Clock } from '../../core/clock';
import { formatMoney, officeTime, shortDate } from '../../core/format';
import { OfficeOverview } from '../../core/models';
import { OverviewStore } from '../../core/overview.store';
import { pollWhileAlive } from '../../core/poll';
import { SessionStore } from '../../core/session';
import { Toaster } from '../../core/toast';
import { BoxStatus } from '../../shared/box-status';
import { BudgetStrip } from '../../shared/budget-strip';
import { chipClass, refillChip } from '../../shared/status';

@Component({
  selector: 'app-dashboard',
  imports: [BoxStatus, BudgetStrip, RouterLink, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  protected readonly store = inject(OverviewStore);
  protected readonly session = inject(SessionStore);
  private readonly clock = inject(Clock);
  private readonly api = inject(ApiClient);
  private readonly toaster = inject(Toaster);

  protected readonly editingBudgetFor = signal<string | null>(null);
  protected budgetDraft = 0;

  protected readonly greeting = computed(() => {
    const me = this.session.me();
    if (!me) return 'Who refills what, and what is left in the budget.';
    const mine = this.store.offices().flatMap(o => o.counters).some(c =>
      c.activeRefill?.assigneeId === me.id && ['Offered', 'Committed'].includes(c.activeRefill.status));
    return mine ? `${me.displayName}, a snack box needs you.` : 'Who refills what, and what is left in the budget.';
  });

  constructor() {
    pollWhileAlive(() => this.store.refresh(), 5000);
  }

  protected refresh = () => void this.store.refresh();
  protected money = formatMoney;
  protected chip = (status: Parameters<typeof refillChip>[0]) => refillChip(status);
  protected chipClass = chipClass;
  protected when = shortDate;

  protected localTime(office: OfficeOverview): string {
    return new Intl.DateTimeFormat('en-GB', { timeZone: office.timeZoneId, hour: '2-digit', minute: '2-digit' }).format(this.clock.now());
  }

  protected officeName(id: string): string { return this.store.officeById().get(id)?.name ?? ''; }

  protected isAdminOf(office: OfficeOverview): boolean {
    const me = this.session.me();
    return !!me?.isOfficeAdmin && me.homeOfficeId === office.id;
  }

  protected startBudgetEdit(office: OfficeOverview): void {
    this.budgetDraft = office.budget.limit.amount;
    this.editingBudgetFor.set(office.id);
  }

  protected async saveBudget(office: OfficeOverview): Promise<void> {
    try {
      const status = await this.api.setBudget(office.id, office.budget.month, this.budgetDraft);
      this.toaster.good(`${office.name} budget for this month is now ${formatMoney(status.limit)}.`);
      this.editingBudgetFor.set(null);
    } catch { /* toast already shown */ }
    await this.store.refresh();
  }

  protected deadline(office: OfficeOverview, iso: string | null): string { return officeTime(iso, office.timeZoneId); }
}
