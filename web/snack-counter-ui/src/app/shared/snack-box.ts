import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type BoxState = 'full' | 'empty' | 'filling' | 'hold';

/**
 * The snack box itself: full when stocked, empty when it needs someone, half full with stripes while a
 * refill is on the way, taped shut when on hold. The fill rises when the box is refilled (the one bit
 * of motion on the page, and it answers a real change).
 */
@Component({
  selector: 'app-snack-box',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="box" [attr.data-state]="state()" role="img" [attr.aria-label]="label()">
      <span class="flap left"></span>
      <span class="flap right"></span>
      <span class="body"><span class="contents"></span></span>
    </div>
  `,
  styles: `
    :host { display: block; flex: none; }
    .box { position: relative; width: 124px; height: 120px; --fill: 0%; }
    .box[data-state='full'] { --fill: 84%; }
    .box[data-state='filling'] { --fill: 42%; }
    .body {
      position: absolute; inset: auto 0 0 0; height: 92px;
      border: 3px solid var(--ink); border-radius: 4px 4px 12px 12px;
      background: var(--surface); overflow: hidden;
    }
    .flap {
      position: absolute; top: 16px; width: 50%; height: 20px;
      border: 3px solid var(--ink); background: var(--surface); border-radius: 3px;
      transition: transform 400ms ease;
    }
    .flap.left { left: 0; transform-origin: 0 100%; transform: rotate(-24deg); }
    .flap.right { right: 0; transform-origin: 100% 100%; transform: rotate(24deg); }
    .box[data-state='hold'] .flap.left,
    .box[data-state='hold'] .flap.right { transform: rotate(0deg); }
    .contents {
      position: absolute; inset: auto 0 0 0; height: var(--fill);
      background:
        radial-gradient(ellipse 11px 7px at 50% 50%, #ffd84d 0 60%, transparent 66%) 0 0 / 24px 16px,
        radial-gradient(ellipse 11px 7px at 50% 50%, #d99f00 0 60%, transparent 66%) 12px 8px / 24px 16px,
        var(--snack);
      transition: height 700ms cubic-bezier(0.2, 0.8, 0.2, 1);
    }
    .box[data-state='filling'] .contents {
      background-image:
        repeating-linear-gradient(-45deg, transparent 0 9px, rgb(255 255 255 / 0.55) 9px 15px),
        radial-gradient(ellipse 11px 7px at 50% 50%, #ffd84d 0 60%, transparent 66%);
      background-size: auto, 24px 16px;
    }
    .box[data-state='empty'] .body { border-style: dashed; }
    .box[data-state='hold'] .body::after {
      content: ''; position: absolute; left: -10%; right: -10%; top: 38%; height: 18px;
      background: var(--berry); transform: rotate(-8deg);
    }
  `,
})
export class SnackBox {
  readonly state = input.required<BoxState>();
  protected readonly label = computed(() => ({
    full: 'Snack box is stocked',
    empty: 'Snack box is empty',
    filling: 'Snack box refill is on the way',
    hold: 'Snack box refill is on hold',
  })[this.state()]);
}
