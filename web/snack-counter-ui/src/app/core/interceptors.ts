import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { Problem } from './models';
import { ActingAs } from './session';
import { Toaster } from './toast';

/** Adds the demo identity header. Production would attach the SSO bearer token here instead. */
export const actingAsInterceptor: HttpInterceptorFn = (req, next) => {
  const id = inject(ActingAs).id();
  return next(id ? req.clone({ setHeaders: { 'X-Employee-Id': id } }) : req);
};

/** Friendly copy for the API's stable error codes. Unknown codes fall back to the server's message. */
const FRIENDLY: Record<string, string> = {
  'refill.already_taken': 'Someone else already took this refill.',
  'refill.concurrent_update': 'Someone else changed this refill at the same moment. Showing the latest.',
  'refill.not_offered_to_you': 'This refill is offered to someone else right now.',
  'refill.invalid_transition': 'This refill has moved on. Showing the latest.',
  'refill.wrong_currency': 'Enter the amount in the currency of the receipt for this office.',
  'refill.not_assignee': 'Only the person buying can upload the receipt.',
  'budget.exhausted': "This month's budget is used up, so the refill is on hold. The office admin has been told.",
  'employee.not_office_admin': 'Only the office admin can do that.',
  'employee.not_volunteer': 'Only volunteers can take a refill.',
  'availability.overlaps': 'Those dates overlap with something you already added.',
};

export function problemMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'Something went wrong. Try again.';
  if (error.status === 0) return "Can't reach the server. Is the API running on port 5252?";
  const problem = (typeof error.error === 'object' ? error.error : null) as Problem | null;
  return (problem?.errorCode && FRIENDLY[problem.errorCode]) || problem?.title || `Request failed (${error.status}).`;
}

/** Every failed request shows a toast. Callers still receive the error to stop spinners or refresh. */
export const problemToastInterceptor: HttpInterceptorFn = (req, next) => {
  const toaster = inject(Toaster);
  return next(req).pipe(
    catchError((error: unknown) => {
      const quiet = req.url.includes('/api/dev/clock'); // optional endpoint, absent outside dev
      if (!quiet) toaster.bad(problemMessage(error));
      return throwError(() => error);
    }),
  );
};
