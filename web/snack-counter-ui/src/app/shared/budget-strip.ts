import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { formatMoney } from '../core/format';
import { BudgetStatus } from '../core/models';

/** This month's budget as one strip: spent, set aside for a refill in progress, and what's left. */
@Component({
  selector: 'app-budget-strip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="strip">
      <p class="line">
        <strong>{{ monthName() }} budget</strong>
        @if (over()) {
          <span class="over">{{ money(overBy()) }} over the limit of {{ money(budget().limit) }}</span>
        } @else {
          <span>{{ money(budget().remaining) }} left of {{ money(budget().limit) }}</span>
        }
      </p>
      <div class="bar" aria-hidden="true">
        <span class="spent" [style.width.%]="spentPct()"></span>
        <span class="reserved" [style.width.%]="reservedPct()"></span>
      </div>
      <p class="legend small muted">
        <span><i class="key spent"></i>{{ money(budget().spent) }} spent</span>
        @if (budget().reserved.amount > 0) {
          <span><i class="key reserved"></i>{{ money(budget().reserved) }} set aside for the refill in progress</span>
        }
      </p>
    </div>
  `,
  styles: `
    .line { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 4px 16px; font-variant-numeric: tabular-nums; }
    .over { color: var(--berry); font-weight: 700; }
    .bar { display: flex; height: 12px; margin: 10px 0 8px; border-radius: 999px; background: var(--paper); border: 1px solid var(--line); overflow: hidden; }
    .spent { background: var(--ink); }
    .reserved { background: repeating-linear-gradient(-45deg, var(--snack) 0 6px, #ffd84d 6px 12px); }
    .legend { display: flex; flex-wrap: wrap; gap: 4px 18px; }
    .key { display: inline-block; width: 10px; height: 10px; border-radius: 3px; margin-right: 6px; vertical-align: -1px; }
    .key.spent { background: var(--ink); }
    .key.reserved { background: var(--snack); }
  `,
})
export class BudgetStrip {
  readonly budget = input.required<BudgetStatus>();

  protected readonly over = computed(() => this.budget().remaining.amount < 0);
  protected readonly overBy = computed(() => ({ ...this.budget().remaining, amount: -this.budget().remaining.amount }));
  protected readonly spentPct = computed(() => this.pct(this.budget().spent.amount));
  protected readonly reservedPct = computed(() => Math.min(this.pct(this.budget().reserved.amount), 100 - this.spentPct()));
  protected readonly monthName = computed(() => {
    const [year, month] = this.budget().month.split('-').map(Number);
    return new Intl.DateTimeFormat('en-GB', { month: 'long' }).format(new Date(year, month - 1, 1));
  });

  protected money = formatMoney;

  private pct(amount: number): number {
    const limit = this.budget().limit.amount;
    return limit <= 0 ? 100 : Math.max(0, Math.min(100, (amount / limit) * 100));
  }
}
