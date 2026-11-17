import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { ApiClient } from './api';

/**
 * "Now", in step with the server. The demo can move the server clock forward (to show a 2-hour offer
 * timing out), so countdowns use the server's time, not the laptop's. Falls back to local time when
 * the dev clock endpoint isn't available.
 */
@Injectable({ providedIn: 'root' })
export class Clock {
  private readonly api = inject(ApiClient);
  private offsetMs = 0;

  readonly now = signal(Date.now());

  constructor() {
    const tick = setInterval(() => this.now.set(Date.now() + this.offsetMs), 1000);
    const sync = setInterval(() => void this.sync(), 15000);
    inject(DestroyRef).onDestroy(() => { clearInterval(tick); clearInterval(sync); });
    void this.sync();
  }

  async sync(): Promise<void> {
    try {
      const info = await this.api.clock();
      this.offsetMs = Date.parse(info.now) - Date.now();
    } catch {
      this.offsetMs = 0;
    }
    this.now.set(Date.now() + this.offsetMs);
  }
}
