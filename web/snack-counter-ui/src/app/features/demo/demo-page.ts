import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiClient } from '../../core/api';
import { Clock } from '../../core/clock';
import { officeTime, shortDate } from '../../core/format';
import { ChatAction, ChatFeedItem, HrClaim } from '../../core/models';
import { OverviewStore } from '../../core/overview.store';
import { pollWhileAlive } from '../../core/poll';
import { SessionStore } from '../../core/session';
import { Toaster } from '../../core/toast';

type Job = 'sweep' | 'outbox' | 'claims' | 'resume';

/**
 * Stand-ins for the outside world, for the walkthrough: what the chat tool and HR system received,
 * buttons to answer as them, and a clock that can be moved forward to show timeouts.
 */
@Component({
  selector: 'app-demo-page',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './demo-page.html',
  styleUrl: './demo-page.scss',
})
export class DemoPage {
  private readonly api = inject(ApiClient);
  private readonly clock = inject(Clock);
  private readonly overview = inject(OverviewStore);
  protected readonly session = inject(SessionStore);
  private readonly toaster = inject(Toaster);

  protected readonly messages = signal<ChatFeedItem[]>([]);
  protected readonly hrClaims = signal<HrClaim[]>([]);
  protected readonly serverNow = signal<string>('');
  protected readonly unavailable = signal(false);
  protected command = '/snacks status';

  protected readonly jobs: { job: Job; label: string; what: string }[] = [
    { job: 'sweep', label: 'Check deadlines', what: 'Hands on unanswered offers and missed purchases. Runs every minute.' },
    { job: 'outbox', label: 'Deliver messages', what: 'Sends queued chat messages and HR claims. Runs every 5 seconds.' },
    { job: 'claims', label: 'Check HR', what: 'Pulls claim status from HR and flags anything over 10 days. Runs hourly.' },
    { job: 'resume', label: 'Resume on-hold refills', what: 'Restarts refills paused for budget, if there is room now.' },
  ];

  constructor() {
    if (!this.overview.loaded()) void this.overview.refresh();
    pollWhileAlive(() => this.load(), 3000);
  }

  async load(): Promise<void> {
    try {
      const [messages, claims, clock] = await Promise.all([this.api.chatFeed(), this.api.hrClaims(), this.api.clock()]);
      this.messages.set(messages);
      this.hrClaims.set(claims);
      this.serverNow.set(clock.now);
      this.unavailable.set(false);
    } catch {
      this.unavailable.set(true);
    }
  }

  protected audience(m: ChatFeedItem): string {
    if (m.audience.kind === 'direct') return `To ${this.session.name(m.audience.id)}`;
    const office = this.overview.officeById().get(m.audience.id)?.name ?? 'Office';
    return m.audience.kind === 'channel' ? `${office} channel` : `${office} admins`;
  }

  /** Clicking a chat button answers as the person it was sent to (or as you, for channel messages). */
  protected async press(m: ChatFeedItem, action: ChatAction): Promise<void> {
    const as = m.audience.kind === 'direct' ? this.session.byId().get(m.audience.id) : this.session.me();
    if (!as) return;
    await this.sendChat(as.chatUserId, action.value, as.displayName);
  }

  protected async sendCommand(): Promise<void> {
    const me = this.session.me();
    if (me && this.command.trim()) await this.sendChat(me.chatUserId, this.command.trim(), me.displayName);
  }

  protected async hr(claim: HrClaim, action: 'approve' | 'pay' | 'reject'): Promise<void> {
    try {
      if (action === 'approve') await this.api.hrApprove(claim.hrClaimId);
      if (action === 'pay') await this.api.hrPay(claim.hrClaimId);
      if (action === 'reject') await this.api.hrReject(claim.hrClaimId, 'Receipt is unreadable');
      await this.api.runJob('claims'); // let the poller pick it up straight away
      await this.api.runJob('outbox');
      this.toaster.info(`HR ${action === 'pay' ? 'paid' : action === 'approve' ? 'approved' : 'rejected'} ${claim.hrClaimId}.`);
    } catch { /* toast already shown */ }
    await this.load();
  }

  protected async advance(minutes: number): Promise<void> {
    try {
      await this.api.advanceClock(minutes);
      await this.clock.sync();
      await this.api.runJob('sweep');
      await this.api.runJob('outbox');
      this.toaster.info(`Moved the clock forward ${minutes >= 60 ? minutes / 60 + ' h' : minutes + ' min'} and checked deadlines.`);
    } catch { /* toast already shown */ }
    await Promise.all([this.load(), this.overview.refresh()]);
  }

  protected async run(job: Job, label: string): Promise<void> {
    try {
      await this.api.runJob(job);
      this.toaster.info(`${label}: done.`);
    } catch { /* toast already shown */ }
    await Promise.all([this.load(), this.overview.refresh()]);
  }

  private async sendChat(chatUserId: string, text: string, who: string): Promise<void> {
    try {
      const reply = await this.api.chat(chatUserId, text);
      this.toaster.info(`Reply to ${who}: ${reply.text}`);
      await this.api.runJob('outbox');
    } catch { /* toast already shown */ }
    await Promise.all([this.load(), this.overview.refresh()]);
  }

  protected readonly offices = this.overview.offices;
  protected officeNow = (timeZone: string) => officeTime(this.serverNow() || this.clock.now(), timeZone, true);
  protected when = shortDate;
  protected hrStatus = (s: HrClaim['status']) => ({ Received: 'Waiting for approval', Approved: 'Approved', Rejected: 'Rejected', PaidOut: 'Paid out' })[s];
}
