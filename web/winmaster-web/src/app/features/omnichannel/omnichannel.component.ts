import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';

@Component({
  selector: 'app-omnichannel',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="page-head">
      <div><h1>Omnichannel</h1><p class="muted">Facebook Messenger, Instagram, Email and AI lead intelligence</p></div>
      <button class="btn btn-primary" (click)="showAccount.set(true)">+ Connect channel</button>
    </div>

    @if (analytics(); as a) {
      <div class="stat-grid">
        <div class="card stat"><div class="l">Total leads</div><div class="n">{{ a.totalLeads }}</div></div>
        <div class="card stat"><div class="l">Qualified</div><div class="n">{{ a.qualifiedLeads }}</div></div>
        <div class="card stat"><div class="l">Converted</div><div class="n">{{ a.convertedLeads }}</div></div>
        <div class="card stat"><div class="l">Conversion rate</div><div class="n">{{ a.conversionRate }}%</div><div class="muted">Value: {{ a.totalConversionValue }}</div></div>
      </div>
      <div class="card card-pad" style="margin-top:1rem">
        <h3>Conversion by channel</h3>
        <table class="table"><thead><tr><th>Channel</th><th>Leads</th><th>Converted</th><th>Rate</th><th>Value</th></tr></thead>
          <tbody>@for (c of a.byChannel; track c.channel) { <tr><td>{{ c.channel }}</td><td>{{ c.leads }}</td><td>{{ c.converted }}</td><td>{{ c.conversionRate }}%</td><td>{{ c.value }}</td></tr> }</tbody>
        </table>
      </div>
    }

    <div class="grid" style="grid-template-columns:1fr 1fr;margin-top:1rem;align-items:start">
      <div class="card card-pad"><div class="between"><h3>Connected channels</h3><span class="badge badge-blue">Unified inbox</span></div>
        @for (c of accounts(); track c.id) { <div class="between" style="padding:.75rem 0;border-bottom:1px solid var(--border)"><div><b>{{ c.displayName }}</b><div class="muted">{{ c.channel }} · {{ c.emailAddress || c.externalAccountId || 'configured' }}</div></div><button class="btn btn-danger btn-sm" (click)="removeAccount(c.id)">Remove</button></div> }
        @if (!accounts().length) { <div class="empty">No extra channels configured</div> }
      </div>
      <div class="card card-pad"><h3>Leads</h3>
        <div class="flex gap" style="margin-bottom:.8rem"><select class="input" [(ngModel)]="leadStatus" (change)="loadLeads()"><option value="">All statuses</option><option>New</option><option>Qualified</option><option>Contacted</option><option>Converted</option><option>Lost</option></select></div>
        <table class="table"><thead><tr><th>Lead</th><th>Channel</th><th>Status</th><th>Score</th></tr></thead><tbody>
          @for (l of leads(); track l.id) { <tr><td>{{ l.displayName }}<div class="muted">{{ l.email || l.phone }}</div></td><td>{{ l.channel }}</td><td><span class="badge" [class]="badge(l.status)">{{ l.status }}</span></td><td>{{ l.score }}</td></tr> }
          @if (!leads().length) { <tr><td colspan="4" class="empty">No leads yet</td></tr> }
        </tbody></table>
      </div>
    </div>

    @if (showAccount()) { <div class="modal-backdrop" (click)="showAccount.set(false)"><div class="modal" (click)="$event.stopPropagation()"><div class="modal-head">Connect channel</div><div class="modal-body">
      <div class="field"><label>Channel</label><select class="input" [(ngModel)]="accountForm.channel"><option>FacebookMessenger</option><option>Instagram</option><option>Email</option></select></div>
      <div class="field"><label>Display name</label><input class="input" [(ngModel)]="accountForm.displayName" /></div>
      @if (accountForm.channel === 'Email') { <div class="field"><label>Email address</label><input class="input" [(ngModel)]="accountForm.emailAddress" /><label>SMTP host</label><input class="input" [(ngModel)]="accountForm.smtpHost" style="margin-top:.35rem" /><label>SMTP port</label><input class="input" type="number" [(ngModel)]="accountForm.smtpPort" style="margin-top:.35rem" /><label>Username</label><input class="input" [(ngModel)]="accountForm.username" style="margin-top:.35rem" /><label>Password</label><input class="input" type="password" [(ngModel)]="accountForm.password" style="margin-top:.35rem" /></div> } @else { <div class="field"><label>Page / Instagram account ID</label><input class="input" [(ngModel)]="accountForm.externalAccountId" /><label>Access token</label><input class="input" type="password" [(ngModel)]="accountForm.accessToken" style="margin-top:.35rem" /></div> }
    </div><div class="modal-foot"><button class="btn btn-ghost" (click)="showAccount.set(false)">Cancel</button><button class="btn btn-primary" (click)="connectAccount()">Save channel</button></div></div></div> }
  `
})
export class OmnichannelComponent implements OnInit {
  private api = inject(ApiService); private toast = inject(ToastService);
  accounts = signal<any[]>([]); leads = signal<any[]>([]); analytics = signal<any>(null); showAccount = signal(false); leadStatus = '';
  accountForm: any = { channel: 'FacebookMessenger', displayName: '', externalAccountId: '', accessToken: '', emailAddress: '', smtpHost: '', smtpPort: 587, username: '', password: '', isActive: true };
  ngOnInit() { this.load(); }
  load() { this.api.channelAccounts().subscribe({ next: x => this.accounts.set(x), error: () => {} }); this.api.leadAnalytics().subscribe({ next: x => this.analytics.set(x), error: () => {} }); this.loadLeads(); }
  loadLeads() { this.api.leads({ status: this.leadStatus, pageSize: 50 }).subscribe({ next: x => this.leads.set(x.items || []), error: () => {} }); }
  connectAccount() { this.api.createChannelAccount(this.accountForm).subscribe({ next: () => { this.toast.success('Channel connected'); this.showAccount.set(false); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Could not connect') }); }
  removeAccount(id: number) { this.api.deleteChannelAccount(id).subscribe(() => { this.toast.success('Removed'); this.load(); }); }
  badge(s: string) { return { Converted: 'badge-green', Qualified: 'badge-blue', Lost: 'badge-red', Contacted: 'badge-amber' }[s] || 'badge-gray'; }
}
