import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { AuthService } from '../../core/auth.service';
import { License, Activation } from '../../core/models';

@Component({
  selector: 'app-licenses',
  standalone: true,
  imports: [FormsModule, DatePipe],
  template: `
  <div class="page-head"><h1>Licenses</h1><button class="btn btn-primary" (click)="open()">+ Generate License</button></div>
  <div class="card card-pad" style="margin-bottom:1rem"><div class="flex gap">
    <input class="input" placeholder="Search key…" [(ngModel)]="q" (keyup.enter)="load()" style="max-width:260px" />
    <select class="input" [(ngModel)]="status" (change)="load()" style="max-width:160px"><option value="">All</option><option>Active</option><option>Revoked</option><option>Expired</option></select>
  </div></div>
  <div class="card">
    <table class="table">
      <thead><tr><th>Key</th><th>Type</th><th>Assigned</th><th>Activations</th><th>Expires</th><th>Status</th><th></th></tr></thead>
      <tbody>
        @for (l of items(); track l.id) {
          <tr><td><code>{{ l.key }}</code></td><td><span class="badge badge-gray">{{ l.licenseType }}</span></td><td>{{ l.userEmail || '—' }}</td>
          <td>{{ l.activationCount }}/{{ l.maxActivations }}</td><td class="muted">{{ l.expiresAt ? (l.expiresAt | date:'mediumDate') : 'Never' }}</td>
          <td><span class="badge" [class]="badge(l.status)">{{ l.status }}</span></td>
          <td class="row-actions"><button class="btn btn-ghost btn-sm" (click)="devices(l)">Devices</button>@if (l.status === 'Active') { <button class="btn btn-danger btn-sm" (click)="revoke(l)">Revoke</button> }</td></tr>
        }
        @if (!items().length && !loading()) { <tr><td colspan="7" class="empty">No licenses</td></tr> }
      </tbody>
    </table>
  </div>

  @if (show()) {
    <div class="modal-backdrop" (click)="show.set(false)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">Generate license</div>
      <div class="modal-body">
        <div class="field"><label>Type</label><select class="input" [(ngModel)]="form.licenseType">@for (t of issuable(); track t) { <option>{{ t }}</option> }</select></div>
        <div class="field"><label>Features (comma separated)</label><input class="input" [(ngModel)]="features" /></div>
        <div class="flex gap"><div class="field" style="flex:1"><label>Max activations</label><input class="input" type="number" [(ngModel)]="form.maxActivations" /></div><div class="field" style="flex:1"><label>Expires</label><input class="input" type="date" [(ngModel)]="form.expiresAt" /></div></div>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="show.set(false)">Cancel</button><button class="btn btn-primary" (click)="create()">Generate</button></div>
    </div></div>
  }
  @if (deviceLicense(); as l) {
    <div class="modal-backdrop" (click)="deviceLicense.set(null)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">Devices — {{ l.key }}</div>
      <div class="modal-body">
        @for (a of acts(); track a.id) {
          <div class="between" style="padding:.4rem 0;border-bottom:1px solid var(--border)">
            <div><b>{{ a.deviceName || 'Device' }}</b><div class="muted" style="font-size:.75rem">{{ a.hwid }}</div></div>
            @if (!a.revokedAt) { <button class="btn btn-danger btn-sm" (click)="revokeDevice(a)">Revoke</button> } @else { <span class="badge badge-gray">Revoked</span> }
          </div>
        }
        @if (!acts().length) { <div class="empty">No devices activated</div> }
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="deviceLicense.set(null)">Close</button></div>
    </div></div>
  }
  `
})
export class LicensesComponent implements OnInit {
  private api = inject(ApiService); private toast = inject(ToastService); private auth = inject(AuthService);
  items = signal<License[]>([]); loading = signal(true); show = signal(false);
  deviceLicense = signal<License | null>(null); acts = signal<Activation[]>([]);
  q = ''; status = ''; features = 'campaigns,inbox';
  form: any = { licenseType: 'User', maxActivations: 1, expiresAt: '' };

  ngOnInit() { this.load(); }
  load() { this.loading.set(true); this.api.licenses({ q: this.q, status: this.status, pageSize: 100 }).subscribe({ next: r => { this.items.set(r.items); this.loading.set(false); }, error: () => { this.loading.set(false); this.toast.error('API unreachable'); } }); }
  issuable() { const r = this.auth.user()?.role; if (r === 'SuperAdmin') return ['Admin', 'Reseller', 'User']; if (r === 'Admin') return ['Reseller', 'User']; return ['User']; }
  open() { this.form = { licenseType: this.issuable()[this.issuable().length - 1], maxActivations: 1, expiresAt: '' }; this.features = 'campaigns,inbox'; this.show.set(true); }
  create() {
    const b = { licenseType: this.form.licenseType, features: this.features.split(',').map((x: string) => x.trim()), startsAt: new Date().toISOString(), expiresAt: this.form.expiresAt || null, maxActivations: +this.form.maxActivations, userId: null };
    this.api.createLicense(b).subscribe({ next: () => { this.toast.success('License generated'); this.show.set(false); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') });
  }
  revoke(l: License) { if (confirm('Revoke license?')) this.api.revokeLicense(l.id).subscribe(() => { this.toast.success('Revoked'); this.load(); }); }
  devices(l: License) { this.deviceLicense.set(l); this.api.activations(l.id).subscribe(a => this.acts.set(a)); }
  revokeDevice(a: Activation) { this.api.revokeDevice(a.id).subscribe(() => { this.toast.success('Device revoked'); if (this.deviceLicense()) this.devices(this.deviceLicense()!); }); }
  badge(s: string) { return { Active: 'badge-green', Revoked: 'badge-red', Expired: 'badge-amber' }[s] || 'badge-gray'; }
}
