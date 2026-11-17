import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BudgetStatus } from '../core/models';
import { BudgetStrip } from './budget-strip';

const nok = (amount: number) => ({ amount, currency: 'NOK' });

function render(budget: BudgetStatus) {
  TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  const fixture = TestBed.createComponent(BudgetStrip);
  fixture.componentRef.setInput('budget', budget);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return { text: () => (el.textContent ?? '').replace(/ /g, ' '), el };
}

describe('BudgetStrip', () => {
  it('shows what is left and the money set aside for a refill in progress', () => {
    const { el, text } = render({ officeId: 'o', month: '2026-11', limit: nok(6000), spent: nok(850), reserved: nok(1200), remaining: nok(3950) });

    expect(text()).toContain('November budget');
    expect(text()).toContain('NOK 3,950 left of NOK 6,000');
    expect(text()).toContain('NOK 1,200 set aside for the refill in progress');
    expect(parseFloat((el.querySelector('.spent') as HTMLElement).style.width)).toBeCloseTo(14.17, 1); // 850 / 6000
    expect(parseFloat((el.querySelector('.reserved') as HTMLElement).style.width)).toBeCloseTo(20, 1); // 1200 / 6000
  });

  it('says how far over the limit an office is', () => {
    const { el, text } = render({ officeId: 'o', month: '2026-11', limit: nok(1000), spent: nok(1100), reserved: nok(0), remaining: nok(-100) });

    expect(text()).toContain('NOK 100 over the limit of NOK 1,000');
    expect(el.querySelector('.over')).not.toBeNull();
    expect(text()).not.toContain('set aside');
  });
});
