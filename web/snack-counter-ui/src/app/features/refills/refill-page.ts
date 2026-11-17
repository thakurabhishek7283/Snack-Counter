import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiClient } from '../../core/api';
import { formatMoney, officeTime } from '../../core/format';
import { PurchaseItem, RefillDetails } from '../../core/models';
import { OverviewStore } from '../../core/overview.store';
import { pollWhileAlive } from '../../core/poll';
import { SessionStore } from '../../core/session';
import { Toaster } from '../../core/toast';
import { BoxStatus } from '../../shared/box-status';
import { chipClass, claimChip, outcomeText, refillChip } from '../../shared/status';

@Component({
  selector: 'app-refill-page',
  imports: [BoxStatus, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './refill-page.html',
  styleUrl: './refill-page.scss',
})
export class RefillPage implements OnInit {
  private readonly api = inject(ApiClient);
  private readonly overview = inject(OverviewStore);
  private readonly session = inject(SessionStore);
  private readonly toaster = inject(Toaster);

  /** Bound from the route (withComponentInputBinding). */
  readonly id = input.required<string>();

  protected readonly details = signal<RefillDetails | null>(null);
  protected readonly notFound = signal(false);
  protected readonly saving = signal(false);

  protected amount: number | null = null;
  protected items = '';
  protected receipt: File | null = null;
  protected cancelReason = '';

  protected readonly office = computed(() => {
    const d = this.details();
    return d ? this.overview.officeById().get(d.summary.officeId) ?? null : null;
  });
  protected readonly timeZone = computed(() => this.office()?.timeZoneId ?? 'UTC');
  protected readonly currency = computed(() => this.office()?.currency ?? '');

  protected readonly isActive = computed(() => {
    const s = this.details()?.summary.status;
    return !!s && s !== 'Completed' && s !== 'Cancelled';
  });

  /** The current buyer, or a previous one who missed the deadline but bought anyway (never strand their money). */
  protected readonly canUpload = computed(() => {
    const d = this.details();
    const me = this.session.me();
    if (!d || !me) return false;
    if (d.summary.status === 'Committed') return d.summary.assigneeId === me.id;
    return this.isActive() && d.attempts.some(a => a.employeeId === me.id && a.outcome === 'PurchaseDeadlineMissed');
  });

  protected readonly isAdmin = computed(() => {
    const me = this.session.me();
    return !!me?.isOfficeAdmin && me.homeOfficeId === this.details()?.summary.officeId;
  });

  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    // Inputs (the route id) are bound by now, unlike in the constructor.
    pollWhileAlive(() => this.load(), 5000, this.destroyRef);
    if (!this.overview.loaded()) void this.overview.refresh();
  }

  async load(): Promise<void> {
    try {
      this.details.set(await this.api.refill(this.id()));
      this.notFound.set(false);
    } catch {
      if (!this.details()) this.notFound.set(true);
    }
  }

  protected onFile(event: Event): void {
    this.receipt = (event.target as HTMLInputElement).files?.[0] ?? null;
  }

  protected async upload(): Promise<void> {
    if (!this.receipt || !this.amount || this.amount <= 0) {
      this.toaster.bad('Enter the amount on the receipt and attach a photo of it.');
      return;
    }
    this.saving.set(true);
    try {
      const result = await this.api.complete(this.id(), {
        amount: this.amount, currency: this.currency(), receipt: this.receipt, items: this.items,
      });
      this.toaster.good(`Receipt saved. Your claim for ${formatMoney(result.amount, 2)} is on its way to HR.`);
      if (result.overBudget) this.toaster.info('This purchase went over the monthly budget. It is still paid back, and the office admin has been told.');
      this.amount = null;
      this.items = '';
      this.receipt = null;
    } catch { /* toast already shown */ } finally {
      this.saving.set(false);
      await Promise.all([this.load(), this.overview.refresh()]);
    }
  }

  protected async cancel(): Promise<void> {
    if (!this.cancelReason.trim()) {
      this.toaster.bad('Say why the refill is being cancelled.');
      return;
    }
    try {
      await this.api.cancel(this.id(), this.cancelReason.trim());
      this.toaster.info('Refill cancelled.');
    } catch { /* toast already shown */ }
    await Promise.all([this.load(), this.overview.refresh()]);
  }

  protected reload = () => void Promise.all([this.load(), this.overview.refresh()]);
  protected itemsText = (items: PurchaseItem[]) =>
    items.map(i => (i.quantity > 1 ? `${i.name} x${i.quantity}` : i.name)).join(', ');
  protected money = formatMoney;
  protected at = (iso: string | null) => officeTime(iso, this.timeZone(), true);
  protected refillChip = refillChip;
  protected claimChip = claimChip;
  protected chipClass = chipClass;
  protected outcome = outcomeText;
}
