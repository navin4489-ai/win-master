import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { Campaign, CampaignProgress, CampaignRecipient } from '../../core/models';

@Component({
  selector: 'app-campaign-detail',
  standalone: true,
  imports: [RouterLink, DatePipe],
  template: `
  <a routerLink="/campaigns" class="muted">← Campaigns</a>
  @if (campaign(); as c) {
    <div class="page-head" style="margin-top:.6rem">
      <div>
        <h1>{{ c.name }}</h1>
        <span class="badge" [class]="badge(c.status)">{{ c.status }}</span>
        <span class="muted" style="margin-left:.6rem">{{ c.engineType }} · {{ c.groupName }}</span>
      </div>
      <div class="row-actions">
        @if (c.status === 'Draft' || c.status === 'Scheduled' || c.status === 'Paused') { <button class="btn btn-primary" (click)="action('start')">Start</button> }
        @if (c.status === 'Running') { <button class="btn btn-ghost" (click)="action('pause')">Pause</button> }
        @if (c.status === 'Paused') { <button class="btn btn-primary" (click)="action('resume')">Resume</button> }
        @if (c.status === 'Running' || c.status === 'Paused') { <button class="btn btn-danger" (click)="action('stop')">Stop</button> }
        @if (c.status === 'Completed' || c.status === 'Stopped' || c.status === 'Failed') { <button class="btn btn-primary" (click)="action('restart')">↻ Restart</button> }
        <button class="btn btn-ghost" (click)="download('csv')">Export CSV</button>
        <button class="btn btn-ghost" (click)="download('xlsx')">Export XLSX</button>
      </div>
    </div>

    @if (progress(); as p) {
      <div class="card card-pad" style="margin-bottom:1rem">
        <div class="between"><h3>Progress</h3><span class="muted">{{ p.processed }}/{{ p.total }} · ETA {{ eta(p) }}</span></div>
        <div class="progress" style="height:12px"><span [style.width.%]="pct(p)"></span></div>
        <div class="stat-grid" style="margin-top:1rem">
          <div class="card stat"><div class="l">Sent</div><div class="n">{{ p.sent }}</div></div>
          <div class="card stat"><div class="l">Delivered</div><div class="n">{{ p.delivered }}</div></div>
          <div class="card stat"><div class="l">Read</div><div class="n">{{ p.read }}</div></div>
          <div class="card stat"><div class="l">Failed</div><div class="n" style="color:var(--danger)">{{ p.failed }}</div></div>
        </div>
      </div>
    }

    <div class="card">
      <div class="card-pad between"><h3>Recipients</h3></div>
      <table class="table">
        <thead><tr><th>Phone</th><th>Name</th><th>Status</th><th>Sent</th><th>Error</th></tr></thead>
        <tbody>
          @for (r of recipients(); track r.id) {
            <tr><td>{{ r.phoneMasked }}</td><td>{{ r.name }}</td><td><span class="badge" [class]="rbadge(r.status)">{{ r.status }}</span></td><td class="muted">{{ r.sentAt ? (r.sentAt | date:'short') : '—' }}</td><td class="muted">{{ r.error }}</td></tr>
          }
          @if (!recipients().length) { <tr><td colspan="5" class="empty">No recipients materialized yet</td></tr> }
        </tbody>
      </table>
    </div>
  }
  `
})
export class CampaignDetailComponent implements OnInit, OnDestroy {
  private api = inject(ApiService);
  private route = inject(ActivatedRoute);
  private toast = inject(ToastService);
  campaign = signal<Campaign | null>(null);
  progress = signal<CampaignProgress | null>(null);
  recipients = signal<CampaignRecipient[]>([]);
  private timer?: any;
  private id = 0;

  ngOnInit() {
    this.id = +this.route.snapshot.params['id'];
    this.refresh();
    this.timer = setInterval(() => this.poll(), 3000);
  }
  ngOnDestroy() { clearInterval(this.timer); }

  refresh() {
    this.api.campaign(this.id).subscribe(c => this.campaign.set(c));
    this.poll();
    this.api.campaignRecipients(this.id, { pageSize: 100 }).subscribe(r => this.recipients.set(r.items));
  }
  poll() {
    this.api.campaignProgress(this.id).subscribe({ next: p => this.progress.set(p), error: () => {} });
  }
  action(a: string) {
    this.api.campaignAction(this.id, a).subscribe({ next: c => { this.campaign.set(c); this.toast.success(`Campaign ${a}ed`); this.refresh(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') });
  }
  download(fmt: string) {
    this.api.campaignReport(this.id, fmt).subscribe(blob => {
      const url = URL.createObjectURL(blob); const a = document.createElement('a');
      a.href = url; a.download = `campaign-${this.id}.${fmt}`; a.click(); URL.revokeObjectURL(url);
    });
  }
  pct(p: CampaignProgress) { return p.total ? Math.round(p.processed * 100 / p.total) : 0; }
  eta(p: CampaignProgress) { const s = p.estimatedSecondsRemaining || 0; return s > 60 ? `${Math.round(s / 60)}m` : `${s}s`; }
  badge(s: string) { return { Running: 'badge-blue', Completed: 'badge-green', Scheduled: 'badge-amber', Failed: 'badge-red', Paused: 'badge-amber' }[s] || 'badge-gray'; }
  rbadge(s: string) { return { Sent: 'badge-blue', Delivered: 'badge-green', Read: 'badge-green', Failed: 'badge-red', Sending: 'badge-amber' }[s] || 'badge-gray'; }
}
