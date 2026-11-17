import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Person } from './core/models';
import { SessionStore } from './core/session';
import { Toasts } from './core/toast';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Toasts],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a class="skip" href="#main">Skip to content</a>
    <header class="bar">
      <a class="brand" routerLink="/">Snack Counter</a>
      <nav aria-label="Main">
        <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="exactPath">Boxes</a>
        <a routerLink="/me" routerLinkActive="active">You</a>
        <a routerLink="/report" routerLinkActive="active">Report</a>
        <a routerLink="/demo" routerLinkActive="active">Demo</a>
      </nav>
      <div class="who">
        <label for="acting-as">Signed in as</label>
        <select id="acting-as" [value]="session.me()?.id ?? ''" (change)="actAs($event)">
          @for (group of groups(); track group.office) {
            <optgroup [label]="group.office">
              @for (p of group.people; track p.id) {
                <option [value]="p.id" [selected]="p.id === session.me()?.id">{{ p.displayName }}{{ p.isOfficeAdmin ? ' (admin)' : '' }}</option>
              }
            </optgroup>
          }
        </select>
      </div>
    </header>
    <main id="main">
      <router-outlet />
    </main>
    <app-toasts />
  `,
  styles: `
    .skip { position: absolute; left: -999px; top: 8px; background: var(--snack); padding: 8px 12px; z-index: 20; }
    .skip:focus { left: 8px; }
    .bar {
      display: flex; flex-wrap: wrap; align-items: center; gap: 12px 28px;
      padding: 14px var(--gutter);
      background: var(--ink); color: #fff;
    }
    .brand { font: 800 var(--step-1) / 1 var(--font-display); color: #fff; text-decoration: none; letter-spacing: -0.01em; }
    .brand::before {
      content: ''; display: inline-block; width: 14px; height: 14px; margin-right: 10px; vertical-align: 0;
      background: var(--snack); border-radius: 3px; transform: rotate(-8deg);
    }
    nav { display: flex; gap: 4px; flex-wrap: wrap; }
    nav a { color: #d6dbe6; text-decoration: none; padding: 8px 12px; border-radius: 999px; font-weight: 700; }
    nav a:hover { color: #fff; }
    nav a.active { background: var(--snack); color: var(--ink); }
    .who { margin-left: auto; display: flex; align-items: center; gap: 10px; }
    .who label { font-size: var(--step--1); color: #c3c9d6; }
    .who select { min-height: 38px; border-color: transparent; color: var(--ink); }
    :focus-visible { box-shadow: 0 0 0 3px var(--snack); }
    @media (max-width: 720px) { .who { margin-left: 0; width: 100%; } .who select { flex: 1; } }
  `,
})
export class App {
  protected readonly session = inject(SessionStore);
  protected readonly exactPath = { paths: 'exact', queryParams: 'ignored', matrixParams: 'ignored', fragment: 'ignored' } as const;

  protected readonly groups = computed(() => {
    const byOffice = new Map<string, Person[]>();
    for (const p of this.session.people()) {
      byOffice.set(p.homeOfficeName, [...(byOffice.get(p.homeOfficeName) ?? []), p]);
    }
    return [...byOffice].map(([office, people]) => ({ office, people }));
  });

  constructor() {
    void this.session.load().catch(() => undefined);
  }

  protected actAs(event: Event): void {
    this.session.actAs((event.target as HTMLSelectElement).value);
  }
}
