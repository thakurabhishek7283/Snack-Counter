import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import {
  AvailabilityEntry, AvailabilityKind, BudgetStatus, ChatFeedItem, ChatReply, ClaimSummary, ClockInfo, CommitResult,
  CompleteResult, HrClaim, MonthlyReport, OfficeOverview, Person, RefillDetails, RefillSummary, ReportEmptyResult,
} from './models';

/** Typed client for the Snack Counter API. Promise-based so stores can use plain async/await with signals. */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  // ---------- Boxes and refills ----------
  overview() { return this.get<OfficeOverview[]>('/api/overview'); }
  refills(officeId?: string, take = 15) {
    let params = new HttpParams().set('take', take);
    if (officeId) params = params.set('officeId', officeId);
    return this.call(this.http.get<RefillSummary[]>('/api/refills', { params }));
  }
  refill(id: string) { return this.get<RefillDetails>(`/api/refills/${id}`); }
  reportEmpty(counterId: string) { return this.post<ReportEmptyResult>(`/api/counters/${counterId}/refills`); }
  commit(refillId: string) { return this.post<CommitResult>(`/api/refills/${refillId}/commit`); }
  decline(refillId: string) { return this.post<void>(`/api/refills/${refillId}/decline`); }
  cancel(refillId: string, reason: string) { return this.post<void>(`/api/refills/${refillId}/cancel`, { reason }); }

  complete(refillId: string, purchase: { amount: number; currency: string; receipt: File; items?: string }) {
    const form = new FormData();
    form.set('amount', String(purchase.amount));
    form.set('currency', purchase.currency);
    form.set('receipt', purchase.receipt, purchase.receipt.name);
    if (purchase.items?.trim()) form.set('items', purchase.items.trim());
    return this.post<CompleteResult>(`/api/refills/${refillId}/complete`, form);
  }

  // ---------- Budgets and reports ----------
  setBudget(officeId: string, month: string, amount: number) {
    return this.call(this.http.put<BudgetStatus>(`/api/offices/${officeId}/budget/${month}`, { amount }));
  }
  report(month: string, currency: string) {
    return this.call(this.http.get<MonthlyReport>('/api/reports/monthly', { params: { month, currency } }));
  }

  // ---------- People ----------
  people() { return this.get<Person[]>('/api/people'); }
  availability() { return this.get<AvailabilityEntry[]>('/api/me/availability'); }
  addAvailability(entry: { from: string; to: string; kind: AvailabilityKind; visitingOfficeId: string | null }) {
    return this.post<{ id: string }>('/api/me/availability', entry);
  }
  claims() { return this.get<ClaimSummary[]>('/api/me/claims'); }
  resubmit(claimId: string, receipt: File) {
    const form = new FormData();
    form.set('receipt', receipt, receipt.name);
    return this.post<void>(`/api/claims/${claimId}/resubmit`, form);
  }

  // ---------- Chat tool (the same webhook the real chat integration calls) ----------
  chat(chatUserId: string, text: string) { return this.post<ChatReply>('/api/integrations/chat/events', { chatUserId, text }); }

  // ---------- Demo controls (dev only) ----------
  chatFeed() { return this.get<ChatFeedItem[]>('/api/dev/chat/messages'); }
  hrClaims() { return this.get<HrClaim[]>('/api/dev/hr/claims'); }
  hrApprove(id: string) { return this.post<void>(`/api/dev/hr/claims/${id}/approve`); }
  hrPay(id: string) { return this.post<void>(`/api/dev/hr/claims/${id}/pay`); }
  hrReject(id: string, reason: string) { return this.post<void>(`/api/dev/hr/claims/${id}/reject`, { reason }); }
  clock() { return this.get<ClockInfo>('/api/dev/clock'); }
  advanceClock(minutes: number) { return this.post<ClockInfo>('/api/dev/clock/advance', { minutes }); }
  runJob(job: 'sweep' | 'outbox' | 'claims' | 'resume') { return this.post<unknown>(`/api/dev/jobs/${job}`); }

  private get<T>(url: string) { return this.call(this.http.get<T>(url)); }
  private post<T>(url: string, body: unknown = null) { return this.call(this.http.post<T>(url, body)); }
  private call<T>(request: Observable<T>): Promise<T> { return firstValueFrom(request); }
}
