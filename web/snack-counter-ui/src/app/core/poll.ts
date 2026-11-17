import { DestroyRef, inject } from '@angular/core';

/**
 * Runs `work` now and then every `ms` until the owner is destroyed, skipping ticks while the tab is hidden.
 * Polling keeps the demo simple. SignalR push is the upgrade path if it ever matters.
 * Call from a constructor, or pass the DestroyRef when calling later (e.g. from ngOnInit, once inputs are bound).
 */
export function pollWhileAlive(work: () => Promise<unknown> | void, ms: number, destroyRef: DestroyRef = inject(DestroyRef)): void {
  const run = () => { if (document.visibilityState === 'visible') void work(); };
  run();
  const timer = setInterval(run, ms);
  document.addEventListener('visibilitychange', run);
  destroyRef.onDestroy(() => {
    clearInterval(timer);
    document.removeEventListener('visibilitychange', run);
  });
}
