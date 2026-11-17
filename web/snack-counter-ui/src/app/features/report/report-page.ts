import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiClient } from '../../core/api';
import { currentMonth, formatMoney, formatTimeSpan } from '../../core/format';
import { MonthlyReport } from '../../core/models';

/** Management's one monthly report: each office in its own currency, plus one total. */
@Component({
  selector: 'app-report-page',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './report-page.html',
  styleUrl: './report-page.scss',
})
export class ReportPage {
  private readonly api = inject(ApiClient);

  protected month = currentMonth();
  protected currency = 'NOK';
  protected readonly report = signal<MonthlyReport | null>(null);
  protected readonly loading = signal(false);

  protected readonly monthLabel = computed(() => {
    const r = this.report();
    if (!r) return '';
    const [y, m] = r.month.split('-').map(Number);
    return new Intl.DateTimeFormat('en-GB', { month: 'long', year: 'numeric' }).format(new Date(y, m - 1, 1));
  });
  protected readonly rates = computed(() => Object.entries(this.report()?.ratesUsed ?? {}));

  constructor() { void this.load(); }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.report.set(await this.api.report(this.month, this.currency));
    } catch { /* toast already shown */ } finally {
      this.loading.set(false);
    }
  }

  protected money = formatMoney;
  protected span = formatTimeSpan;
}
