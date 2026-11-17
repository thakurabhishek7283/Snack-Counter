import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { actingAsInterceptor, problemMessage, problemToastInterceptor } from './interceptors';
import { ActingAs } from './session';
import { Toaster } from './toast';

describe('problemMessage', () => {
  it('turns stable error codes into friendly copy', () => {
    const error = new HttpErrorResponse({ status: 409, error: { title: 'This refill has already been taken by employee 0e00...', errorCode: 'refill.already_taken' } });
    expect(problemMessage(error)).toBe('Someone else already took this refill.');
  });

  it("falls back to the server's message for codes it doesn't know", () => {
    const error = new HttpErrorResponse({ status: 422, error: { title: 'Only NOK 200 left this month.', errorCode: 'budget.something_new' } });
    expect(problemMessage(error)).toBe('Only NOK 200 left this month.');
  });

  it('explains an unreachable server', () => {
    expect(problemMessage(new HttpErrorResponse({ status: 0 }))).toContain("Can't reach the server");
  });
});

describe('HTTP interceptors', () => {
  const toaster = { bad: vi.fn(), good: vi.fn(), info: vi.fn() };

  function setup(actingAs: string | null) {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([actingAsInterceptor, problemToastInterceptor])),
        provideHttpClientTesting(),
        { provide: Toaster, useValue: toaster },
      ],
    });
    if (actingAs) TestBed.inject(ActingAs).set(actingAs);
    return { http: TestBed.inject(HttpClient), backend: TestBed.inject(HttpTestingController) };
  }

  beforeEach(() => toaster.bad.mockClear());

  it('says who is acting on every request', async () => {
    const { http, backend } = setup('lars-id');

    const done = firstValueFrom(http.get('/api/overview'));
    backend.expectOne('/api/overview').flush([]);
    await done;

    backend.verify();
  });

  it('adds the X-Employee-Id header', () => {
    const { http, backend } = setup('lars-id');

    http.get('/api/me').subscribe();
    const request = backend.expectOne('/api/me');

    expect(request.request.headers.get('X-Employee-Id')).toBe('lars-id');
    request.flush({});
  });

  it('shows a toast for a failed request and still passes the error on', async () => {
    const { http, backend } = setup('lars-id');

    const result = firstValueFrom(http.post('/api/refills/r1/commit', null)).catch((e: unknown) => e);
    backend.expectOne('/api/refills/r1/commit').flush(
      { title: 'taken', errorCode: 'refill.already_taken' },
      { status: 409, statusText: 'Conflict' });

    expect(await result).toBeInstanceOf(HttpErrorResponse);
    expect(toaster.bad).toHaveBeenCalledWith('Someone else already took this refill.');
  });
});
