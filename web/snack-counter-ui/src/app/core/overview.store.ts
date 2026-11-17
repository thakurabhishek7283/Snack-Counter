import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiClient } from './api';
import { OfficeOverview, RefillSummary } from './models';

/**
 * The dashboard's state: every office, its budget, its boxes, and recent refills.
 * Signals in, signals out. Components never call the API for this data directly.
 */
@Injectable({ providedIn: 'root' })
export class OverviewStore {
  private readonly api = inject(ApiClient);

  readonly offices = signal<OfficeOverview[]>([]);
  readonly recent = signal<RefillSummary[]>([]);
  readonly loaded = signal(false);
  readonly unreachable = signal(false);
  readonly officeById = computed(() => new Map(this.offices().map(o => [o.id, o])));

  async refresh(): Promise<void> {
    try {
      const [offices, recent] = await Promise.all([this.api.overview(), this.api.refills(undefined, 12)]);
      this.offices.set(offices);
      this.recent.set(recent);
      this.unreachable.set(false);
    } catch {
      this.unreachable.set(true);
    } finally {
      this.loaded.set(true);
    }
  }
}
