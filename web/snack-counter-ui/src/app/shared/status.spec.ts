import { RefillStatus, RefillSummary } from '../core/models';
import { boxState, chipClass, claimChip, refillChip } from './status';

const refill = (status: RefillStatus): RefillSummary => ({
  id: 'r1', officeId: 'o1', counterId: 'c1', status, assigneeId: null, assigneeName: null,
  deadlineUtc: null, reportedAt: '2026-11-17T08:00:00Z', closedAt: null, amount: null,
});

describe('boxState', () => {
  it('is full when there is no active refill', () => expect(boxState(null)).toBe('full'));
  it('is filling while someone is buying', () => expect(boxState(refill('Committed'))).toBe('filling'));
  it('is taped shut when on hold', () => expect(boxState(refill('OnHold'))).toBe('hold'));

  it.each<RefillStatus>(['Open', 'Offered', 'Escalated'])('is empty while %s', status => {
    expect(boxState(refill(status))).toBe('empty');
  });
});

describe('status chips', () => {
  it('flags everything that needs a volunteer in the attention tone', () => {
    for (const status of ['Open', 'Offered', 'Escalated'] as RefillStatus[]) {
      expect(refillChip(status).tone).toBe('needs');
    }
  });

  it('marks overdue claims as a problem whatever their status', () => {
    expect(claimChip('Submitted', true)).toEqual({ label: 'With HR for over 10 days', tone: 'bad' });
    expect(claimChip('Paid').tone).toBe('good');
  });

  it('maps tones to CSS classes', () => {
    expect(chipClass({ label: 'x', tone: 'plain' })).toBe('chip');
    expect(chipClass({ label: 'x', tone: 'bad' })).toBe('chip chip-bad');
  });
});
