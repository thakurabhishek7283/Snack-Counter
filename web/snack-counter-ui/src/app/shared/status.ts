import { AttemptOutcome, ClaimStatus, RefillStatus, RefillSummary } from '../core/models';
import { BoxState } from './snack-box';

export type Tone = 'needs' | 'good' | 'bad' | 'plain';
export interface Chip { label: string; tone: Tone; }

export function boxState(refill: RefillSummary | null): BoxState {
  if (!refill) return 'full';
  if (refill.status === 'Committed') return 'filling';
  if (refill.status === 'OnHold') return 'hold';
  return 'empty';
}

const REFILL: Record<RefillStatus, Chip> = {
  Open: { label: 'Looking for a volunteer', tone: 'needs' },
  Offered: { label: 'Waiting for an answer', tone: 'needs' },
  Escalated: { label: 'Open to everyone', tone: 'needs' },
  OnHold: { label: 'On hold', tone: 'bad' },
  Committed: { label: 'Being refilled', tone: 'plain' },
  Completed: { label: 'Refilled', tone: 'good' },
  Cancelled: { label: 'Cancelled', tone: 'plain' },
};

export function refillChip(status: RefillStatus): Chip { return REFILL[status]; }

export function claimChip(status: ClaimStatus, overdue = false): Chip {
  if (overdue) return { label: 'With HR for over 10 days', tone: 'bad' };
  return ({
    Pending: { label: 'Sending to HR', tone: 'plain' },
    Submitted: { label: 'With HR', tone: 'plain' },
    Approved: { label: 'Approved by HR', tone: 'plain' },
    Rejected: { label: 'Rejected by HR', tone: 'bad' },
    Paid: { label: 'Paid back', tone: 'good' },
  } satisfies Record<ClaimStatus, Chip>)[status];
}

export function outcomeText(outcome: AttemptOutcome): string {
  return ({
    Pending: 'no answer yet',
    Accepted: 'said yes',
    Declined: "said they can't",
    OfferExpired: "didn't answer in time",
    PurchaseDeadlineMissed: "didn't buy in time",
    Superseded: 'no longer needed',
  } satisfies Record<AttemptOutcome, string>)[outcome];
}

export function chipClass(chip: Chip): string {
  return chip.tone === 'plain' ? 'chip' : `chip chip-${chip.tone}`;
}
