import { ChangeDetectionStrategy, Component, Injectable, inject, signal } from '@angular/core';

export interface Toast { id: number; text: string; tone: 'info' | 'good' | 'bad'; }

@Injectable({ providedIn: 'root' })
export class Toaster {
  private next = 1;
  readonly toasts = signal<Toast[]>([]);

  info(text: string) { this.push(text, 'info'); }
  good(text: string) { this.push(text, 'good'); }
  bad(text: string) { this.push(text, 'bad'); }

  dismiss(id: number) { this.toasts.update(list => list.filter(t => t.id !== id)); }

  private push(text: string, tone: Toast['tone']) {
    const id = this.next++;
    this.toasts.update(list => [...list.slice(-3), { id, text, tone }]);
    setTimeout(() => this.dismiss(id), tone === 'bad' ? 8000 : 5000);
  }
}

@Component({
  selector: 'app-toasts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toasts" aria-live="polite">
      @for (toast of toaster.toasts(); track toast.id) {
        <div class="toast" [class]="'toast toast-' + toast.tone" role="status">
          <span>{{ toast.text }}</span>
          <button type="button" class="close" (click)="toaster.dismiss(toast.id)" aria-label="Dismiss">×</button>
        </div>
      }
    </div>
  `,
  styles: `
    .toasts { position: fixed; right: 16px; bottom: 16px; display: grid; gap: 8px; z-index: 10; max-width: min(420px, calc(100vw - 32px)); }
    .toast { display: flex; gap: 12px; align-items: start; padding: 12px 14px; border-radius: var(--radius-m);
      background: var(--ink); color: #fff; box-shadow: 0 6px 24px rgb(29 36 51 / 0.25); }
    .toast-good { border-left: 6px solid var(--mint); }
    .toast-bad { border-left: 6px solid var(--berry); }
    .toast-info { border-left: 6px solid var(--snack); }
    .close { margin-left: auto; background: none; border: 0; color: inherit; font-size: 20px; line-height: 1; cursor: pointer; }
  `,
})
export class Toasts {
  protected readonly toaster = inject(Toaster);
}
