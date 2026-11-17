import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ApiClient } from '../core/api';
import { Clock } from '../core/clock';
import { Person, RefillStatus, RefillSummary } from '../core/models';
import { SessionStore } from '../core/session';
import { Toaster } from '../core/toast';
import { BoxStatus } from './box-status';

const lars: Person = {
  id: 'lars', displayName: 'Lars', homeOfficeId: 'oslo', homeOfficeName: 'Oslo', payrollCurrency: 'NOK',
  isVolunteer: true, isOfficeAdmin: false, chatUserId: 'lars', defaultOfficeDays: ['Tuesday'],
};

const refill = (status: RefillStatus, assigneeId: string | null, assigneeName: string | null = null): RefillSummary => ({
  id: 'r1', officeId: 'oslo', counterId: 'kitchen', status, assigneeId, assigneeName,
  deadlineUtc: '2026-11-17T10:00:00Z', reportedAt: '2026-11-17T08:00:00Z', closedAt: null, amount: null,
});

function setup(current: RefillSummary | null, me: Person | null = lars) {
  const api = {
    commit: vi.fn().mockResolvedValue({ refillId: 'r1', spendUpTo: { amount: 1200, currency: 'NOK' }, purchaseDeadline: '2026-11-18T08:00:00Z' }),
    decline: vi.fn().mockResolvedValue(undefined),
    reportEmpty: vi.fn().mockResolvedValue({ refillId: 'r1', alreadyReported: false, status: 'Offered', assigneeId: 'lars' }),
  };
  const toaster = { good: vi.fn(), info: vi.fn(), bad: vi.fn() };

  TestBed.configureTestingModule({
    providers: [
      provideZonelessChangeDetection(),
      provideRouter([]),
      { provide: ApiClient, useValue: api },
      { provide: Toaster, useValue: toaster },
      { provide: Clock, useValue: { now: signal(Date.parse('2026-11-17T08:00:00Z')) } },
      { provide: SessionStore, useValue: { me: signal(me), name: () => 'Lars' } },
    ],
  });

  const fixture = TestBed.createComponent(BoxStatus);
  fixture.componentRef.setInput('counterId', 'kitchen');
  fixture.componentRef.setInput('refill', current);
  fixture.componentRef.setInput('timeZone', 'Europe/Oslo');
  fixture.detectChanges();

  const el = fixture.nativeElement as HTMLElement;
  const buttons = () => [...el.querySelectorAll('button')].map(b => b.textContent?.trim());
  return { fixture, el, api, toaster, buttons };
}

describe('BoxStatus', () => {
  it('invites anyone to report a stocked box empty', () => {
    const { el, buttons } = setup(null);

    expect(el.querySelector('.headline')?.textContent).toBe('Stocked');
    expect(buttons()).toEqual(['Report empty box']);
  });

  it('asks the offered volunteer to accept or decline, with the time left', () => {
    const { el, buttons } = setup(refill('Offered', 'lars', 'Lars'));

    expect(el.querySelector('.headline')?.textContent).toBe("It's your turn");
    expect(el.querySelector('.detail')?.textContent).toContain('2 h 0 min');
    expect(buttons()).toEqual(["I'll buy it", "Can't this time"]);
  });

  it('shows everyone else who was asked, without action buttons', () => {
    const { el, buttons } = setup(refill('Offered', 'kari', 'Kari'));

    expect(el.querySelector('.headline')?.textContent).toBe('Kari has been asked');
    expect(buttons()).toEqual([]);
  });

  it('lets any volunteer take a refill that went out to everyone', () => {
    const { buttons } = setup(refill('Escalated', null));
    expect(buttons()).toEqual(["I'll buy it"]);
  });

  it('does not let non-volunteers take an open refill', () => {
    const { buttons } = setup(refill('Escalated', null), { ...lars, isVolunteer: false });
    expect(buttons()).toEqual([]);
  });

  it('points the buyer to the receipt upload', () => {
    const { el } = setup(refill('Committed', 'lars', 'Lars'));

    expect(el.querySelector('.headline')?.textContent).toBe("You're on it");
    expect(el.querySelector('a.btn')?.textContent?.trim()).toBe('Upload receipt');
  });

  it('commits through the API, confirms the spend limit, and asks the page to refresh', async () => {
    const { el, api, toaster, fixture } = setup(refill('Offered', 'lars', 'Lars'));
    let changed = 0;
    fixture.componentInstance.changed.subscribe(() => changed++);

    (el.querySelector('button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(api.commit).toHaveBeenCalledWith('r1');
    expect(String(toaster.good.mock.calls[0][0]).replace(/ /g, ' ')).toContain('Spend up to NOK 1,200');
    expect(changed).toBe(1);
  });

  it('still refreshes when the commit is refused (someone else was faster)', async () => {
    const { el, api, fixture } = setup(refill('Escalated', null));
    api.commit.mockRejectedValueOnce(new Error('409'));
    let changed = 0;
    fixture.componentInstance.changed.subscribe(() => changed++);

    (el.querySelector('button') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(changed).toBe(1);
  });
});
