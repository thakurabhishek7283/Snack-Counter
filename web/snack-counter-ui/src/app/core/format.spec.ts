import { formatMoney, formatTimeSpan, officeTime, timeLeft } from './format';

// Intl uses a non-breaking space between the currency code and the amount (so they never wrap apart).
const plain = (s: string) => s.replace(/ /g, ' ');

describe('formatMoney', () => {
  it('shows the currency code and no decimals by default', () => {
    expect(plain(formatMoney({ amount: 6000, currency: 'NOK' }))).toBe('NOK 6,000');
  });

  it('can show decimals for receipts', () => {
    expect(plain(formatMoney({ amount: 850.5, currency: 'INR' }, 2))).toBe('INR 850.50');
  });

  it('renders nothing for a missing amount', () => {
    expect(formatMoney(null)).toBe('');
  });
});

describe('timeLeft', () => {
  const now = Date.parse('2026-11-17T08:00:00Z');

  it('counts down in hours and minutes', () => {
    expect(timeLeft('2026-11-17T09:52:00Z', now)).toBe('1 h 52 min');
  });

  it('uses days for long windows', () => {
    expect(timeLeft('2026-11-18T10:00:00Z', now)).toBe('1 d 2 h');
  });

  it('says overdue once the deadline has passed', () => {
    expect(timeLeft('2026-11-17T07:59:00Z', now)).toBe('overdue');
  });
});

describe('formatTimeSpan', () => {
  it('reads .NET TimeSpan strings', () => {
    expect(formatTimeSpan('00:04:10')).toBe('4 min');
    expect(formatTimeSpan('02:30:00')).toBe('2 h 30 min');
    expect(formatTimeSpan('1.03:00:00')).toBe('1 d 3 h');
  });
});

describe('officeTime', () => {
  it("shows the time in the office's own time zone", () => {
    const instant = '2026-11-17T08:00:00Z';
    expect(officeTime(instant, 'Europe/Oslo')).toBe('Tue 09:00');
    expect(officeTime(instant, 'Asia/Kolkata')).toBe('Tue 13:30');
  });
});
