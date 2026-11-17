import { Money } from './models';

const moneyFormats = new Map<string, Intl.NumberFormat>();

export function formatMoney(money: Money | null | undefined, decimals = 0): string {
  if (!money) return '';
  const key = `${money.currency}:${decimals}`;
  let format = moneyFormats.get(key);
  if (!format) {
    format = new Intl.NumberFormat('en-GB', {
      style: 'currency', currency: money.currency, currencyDisplay: 'code',
      minimumFractionDigits: decimals, maximumFractionDigits: decimals,
    });
    moneyFormats.set(key, format);
  }
  return format.format(money.amount);
}

/** Time as seen in the office, e.g. "Tue 10:59". */
export function officeTime(iso: string | number | null | undefined, timeZone: string, withDate = false): string {
  if (iso === null || iso === undefined) return '';
  return new Intl.DateTimeFormat('en-GB', {
    timeZone,
    weekday: 'short',
    ...(withDate ? { day: 'numeric', month: 'short' } : {}),
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(iso));
}

export function shortDate(iso: string | null | undefined): string {
  if (!iso) return '';
  return new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(iso));
}

/** "1 h 52 min", "4 min", or "overdue". */
export function timeLeft(deadlineIso: string | null | undefined, nowMs: number): string {
  if (!deadlineIso) return '';
  const ms = Date.parse(deadlineIso) - nowMs;
  if (ms <= 0) return 'overdue';
  const minutes = Math.ceil(ms / 60000);
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);
  const mins = minutes % 60;
  if (days > 0) return `${days} d ${hours} h`;
  if (hours > 0) return `${hours} h ${mins} min`;
  return `${mins} min`;
}

/** .NET TimeSpan string ("1.02:03:04" or "02:03:04") to "1 d 2 h" / "2 h 3 min". */
export function formatTimeSpan(value: string | null | undefined): string {
  if (!value) return '';
  const match = /^(?:(\d+)\.)?(\d+):(\d+):(\d+)/.exec(value);
  if (!match) return value;
  const [, d, h, m] = match;
  const days = Number(d ?? 0);
  const hours = Number(h);
  const mins = Number(m);
  if (days > 0) return `${days} d ${hours} h`;
  if (hours > 0) return `${hours} h ${mins} min`;
  return `${mins} min`;
}

export function currentMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}
