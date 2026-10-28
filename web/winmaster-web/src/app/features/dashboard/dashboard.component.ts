import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { DashboardStats, Campaign } from '../../core/models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink],
  template: `
  <div class="page-head"><h1>Dashboard</h1></div>
  @if (loading()) { <div class="spinner"></div> }
  @if (stats(); as s) {
    <div class="stat-grid">
      <div class="card stat"><div class="l">Campaigns</div><div class="n">{{ s.campaigns.total }}</div><div class="muted">{{ s.campaigns.running }} running · {{ s.campaigns.scheduled }} scheduled</div></div>
      <div class="card stat"><div class="l">Sent Today</div><div class="n">{{ s.messages.sentToday }}</div><div class="muted">{{ s.messages.deliveredToday }} delivered · {{ s.messages.failedToday }} failed</div></div>
      <div class="card stat"><div class="l">Accounts</div><div class="n">{{ s.accounts.connected }}/{{ s.accounts.total }}</div><div class="muted">connected</div></div>
      <div class="card stat"><div class="l">Contacts</div><div class="n">{{ s.contacts.total }}</div><div class="muted">{{ s.contacts.groups }} groups</div></div>
      <div class="card stat"><div class="l">Licenses</div><div class="n">{{ s.licenses.active }}</div><div class="muted">{{ s.licenses.expiringSoon }} expiring soon</div></div>
      <div class="card stat"><div class="l">Inbox</div><div class="n">{{ s.inbox.unread }}</div><div class="muted">{{ s.inbox.threads }} threads</div></div>
    </div>
  }
  <div class="card card-pad" style="margin-top:1.3rem">
    <div class="between"><h3>Recent campaigns</h3><a routerLink="/campaigns" class="btn btn-ghost btn-sm">View all</a></div>
    <table class="table">
      <thead><tr><th>Name</th><th>Status</th><th>Progress</th><th>Recipients</th></tr></thead>
      <tbody>
        @for (c of recent(); track c.id) {
          <tr>
            <td><a [routerLink]="['/campaigns', c.id]">{{ c.name }}</a></td>
            <td><span class="badge" [class]="badge(c.status)">{{ c.status }}</span></td>
            <td style="min-width:140px"><div class="progress"><span [style.width.%]="c.progressPercent"></span></div></td>
            <td>{{ c.processedCount }}/{{ c.totalRecipients }}</td>
          </tr>
        }
        @if (!recent().length) { <tr><td colspan="4" class="empty">No campaigns yet</td></tr> }
      </tbody>
    </table>
  </div>
  `
})
export class DashboardComponent implements OnInit {
  private api = inject(ApiService);
  stats = signal<DashboardStats | null>(null);
  recent = signal<Campaign[]>([]);
  loading = signal(true);

  ngOnInit() {
    this.api.dashboard().subscribe({ next: s => { this.stats.set(s); this.loading.set(false); }, error: () => this.loading.set(false) });
    this.api.campaigns({ page: 1, pageSize: 5 }).subscribe({ next: r => this.recent.set(r.items) });
  }
  badge(s: string) {
    return { Running: 'badge-blue', Completed: 'badge-green', Scheduled: 'badge-amber', Failed: 'badge-red', Paused: 'badge-amber' }[s] || 'badge-gray';
  }
}
