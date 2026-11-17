import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiClient } from '../core/api';
import { Clock } from '../core/clock';
import { formatMoney, officeTime, timeLeft } from '../core/format';
import { RefillSummary } from '../core/models';
import { SessionStore } from '../core/session';
import { Toaster } from '../core/toast';
import { SnackBox } from './snack-box';
import { boxState } from './status';

interface View { headline: string; detail: string; }

/**
 * One snack box: what state it's in, said from the viewer's point of view, with the actions that make
 * sense for them. Every button calls the same use case the chat buttons do.
 */
@Component({
  selector: 'app-box-status',
  imports: [SnackBox, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="box-status">
      <app-snack-box [state]="state()" />
      <div class="text">
        <p class="headline">{{ view().headline }}</p>
        <p class="detail">{{ view().detail }}</p>
        <div class="actions">
          @if (!refill()) {
            <button type="button" class="btn btn-primary" [disabled]="busy()" (click)="reportEmpty()">Report empty box</button>
          }
          @if (canAccept()) {
            <button type="button" class="btn btn-primary" [disabled]="busy()" (click)="accept()">I'll buy it</button>
          }
          @if (offeredToMe()) {
            <button type="button" class="btn btn-quiet" [disabled]="busy()" (click)="decline()">Can't this time</button>
          }
          @if (committedByMe() && showDetailsLink()) {
            <a class="btn btn-primary" [routerLink]="['/refills', refill()!.id]">Upload receipt</a>
          }
        </div>
        @if (refill() && showDetailsLink() && !committedByMe()) {
          <a class="details small" [routerLink]="['/refills', refill()!.id]">Who was asked and what happened</a>
        }
      </div>
    </div>
  `,
  styles: `
    .box-status { display: flex; gap: 24px; align-items: center; }
    .text { display: grid; gap: 8px; min-width: 0; }
    .headline { font: 800 var(--step-3) / 1.05 var(--font-display); letter-spacing: -0.02em; }
    .detail { color: var(--ink-soft); }
    .actions { margin-top: 6px; }
    .actions:empty { display: none; }
    .details { color: var(--muted); justify-self: start; }
    @media (max-width: 520px) {
      .box-status { flex-direction: column; align-items: flex-start; }
      .headline { font-size: var(--step-2); }
    }
  `,
})
export class BoxStatus {
  private readonly api = inject(ApiClient);
  private readonly session = inject(SessionStore);
  private readonly clock = inject(Clock);
  private readonly toaster = inject(Toaster);

  readonly counterId = input.required<string>();
  readonly refill = input<RefillSummary | null>(null);
  readonly timeZone = input.required<string>();
  readonly showDetailsLink = input(true);
  readonly changed = output<void>();

  protected readonly busy = signal(false);
  protected readonly state = computed(() => boxState(this.refill()));

  private readonly meId = computed(() => this.session.me()?.id ?? null);
  protected readonly offeredToMe = computed(() => this.refill()?.status === 'Offered' && this.refill()?.assigneeId === this.meId());
  protected readonly committedByMe = computed(() => this.refill()?.status === 'Committed' && this.refill()?.assigneeId === this.meId());
  protected readonly canAccept = computed(() =>
    this.offeredToMe() || (this.refill()?.status === 'Escalated' && !!this.session.me()?.isVolunteer));

  protected readonly view = computed<View>(() => {
    const r = this.refill();
    const left = timeLeft(r?.deadlineUtc, this.clock.now());
    const name = r?.assigneeName ?? 'Someone';
    if (!r) return { headline: 'Stocked', detail: 'Nothing to do. If you see it empty, report it here or type /snacks empty in chat.' };
    switch (r.status) {
      case 'Offered':
        return this.offeredToMe()
          ? { headline: "It's your turn", detail: `You're next in line. Answer within ${left}, or it goes to the next volunteer.` }
          : { headline: `${name} has been asked`, detail: `Waiting for an answer, ${left} left. If they can't, the next volunteer is asked.` };
      case 'Escalated':
        return { headline: 'Up for grabs', detail: 'Nobody has taken it yet, so it went out to everyone. First to accept gets it.' };
      case 'Committed':
        return this.committedByMe()
          ? { headline: "You're on it", detail: `Buy snacks and upload the receipt within ${left}. Your claim goes to HR automatically.` }
          : { headline: `${name} is on it`, detail: `No need for anyone else to buy. Due by ${officeTime(r.deadlineUtc, this.timeZone())}.` };
      case 'OnHold':
        return { headline: 'On hold', detail: "This month's budget is used up. It starts again when the admin raises the limit or the month ends." };
      default:
        return { headline: 'Looking for a volunteer', detail: 'Checking who is in the office.' };
    }
  });

  async reportEmpty(): Promise<void> {
    await this.run(async () => {
      const result = await this.api.reportEmpty(this.counterId());
      const who = result.assigneeId ? this.session.name(result.assigneeId) : null;
      this.toaster.good(
        result.alreadyReported ? 'Thanks, this was already reported.'
        : who ? `Thanks for reporting it. ${who} has been asked to refill it.`
        : 'Thanks for reporting it. It has gone out to every volunteer.');
    });
  }

  async accept(): Promise<void> {
    await this.run(async () => {
      const result = await this.api.commit(this.refill()!.id);
      this.toaster.good(`You're on it. Spend up to ${formatMoney(result.spendUpTo)} and upload the receipt by ${officeTime(result.purchaseDeadline, this.timeZone())}.`);
    });
  }

  async decline(): Promise<void> {
    await this.run(async () => {
      await this.api.decline(this.refill()!.id);
      this.toaster.info("No problem. It's been passed to the next volunteer.");
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch {
      // The interceptor already showed what went wrong. Refresh to show the latest state.
    } finally {
      this.busy.set(false);
      this.changed.emit();
    }
  }
}
