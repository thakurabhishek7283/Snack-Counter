import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiClient } from './api';
import { Person } from './models';

const STORAGE_KEY = 'snack-counter.acting-as';

function readStored(): string | null {
  try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }
}

function writeStored(id: string): void {
  try { localStorage.setItem(STORAGE_KEY, id); } catch { /* private mode: fine, it just won't be remembered */ }
}

/**
 * Who the user is. Has no dependencies so the HTTP interceptor can read it without a DI cycle.
 * Demo: picked in the header. Production: comes from the company SSO login instead.
 */
@Injectable({ providedIn: 'root' })
export class ActingAs {
  /** `?as=<employee id>` wins, so a demo link can open the app as a specific person. */
  readonly id = signal<string | null>(new URLSearchParams(location.search).get('as') ?? readStored());

  set(id: string): void {
    this.id.set(id);
    writeStored(id);
  }
}

@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly api = inject(ApiClient);
  private readonly actingAs = inject(ActingAs);

  readonly people = signal<Person[]>([]);
  readonly me = computed(() => this.people().find(p => p.id === this.actingAs.id()) ?? null);
  readonly byId = computed(() => new Map(this.people().map(p => [p.id, p])));

  async load(): Promise<void> {
    const people = await this.api.people();
    this.people.set(people);
    if (!people.some(p => p.id === this.actingAs.id())) {
      const fallback = people.find(p => p.isVolunteer) ?? people[0];
      if (fallback) this.actingAs.set(fallback.id);
    }
  }

  actAs(id: string): void { this.actingAs.set(id); }

  name(id: string | null | undefined): string {
    return (id && this.byId().get(id)?.displayName) || 'Someone';
  }
}
