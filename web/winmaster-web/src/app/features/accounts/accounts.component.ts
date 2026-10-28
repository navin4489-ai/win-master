import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { WaAccount, Proxy, AntiBan } from '../../core/models';

@Component({
  selector: 'app-accounts',
  standalone: true,
  imports: [FormsModule],
  template: `
  <div class="page-head"><h1>WhatsApp Accounts</h1><button class="btn btn-primary" (click)="openCreate()">+ Add Account</button></div>
  <div class="stat-grid">
    @for (a of items(); track a.id) {
      <div class="card card-pad">
        <div class="between"><b>{{ a.displayName }}</b><span class="badge" [class]="badge(a.status)">{{ a.status }}</span></div>
        <div class="muted">{{ a.phoneNumber || '—' }} · {{ a.engineType }}</div>
        <div class="muted" style="font-size:.8rem;margin:.4rem 0">Sent today {{ a.sentToday }}/{{ a.dailyLimit }} · warmup {{ a.warmupState }}</div>
        <div class="progress"><span [style.width.%]="pct(a)"></span></div>
        <div class="row-actions" style="margin-top:.7rem">
          @if (a.engineType === 'Baileys' && a.status !== 'Connected') { <button class="btn btn-primary btn-sm" (click)="connect(a)">Connect (QR)</button> }
          @if (a.status === 'Connected') { <button class="btn btn-ghost btn-sm" (click)="disconnect(a)">Disconnect</button> }
          <button class="btn btn-ghost btn-sm" (click)="openAntiBan(a)">Anti-ban</button>
          <button class="btn btn-danger btn-sm" (click)="remove(a)">Delete</button>
        </div>
      </div>
    }
    @if (!items().length && !loading()) { <div class="empty">No accounts</div> }
  </div>

  @if (showCreate()) {
    <div class="modal-backdrop" (click)="showCreate.set(false)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">Add WhatsApp account</div>
      <div class="modal-body">
        <div class="field"><label>Display name</label><input class="input" [(ngModel)]="form.displayName" /></div>
        <div class="field"><label>Engine</label><select class="input" [(ngModel)]="form.engineType"><option>Baileys</option><option>CloudApi</option></select></div>
        <div class="field"><label>Phone number</label><input class="input" [(ngModel)]="form.phoneNumber" placeholder="+1..." /></div>
        <div class="field"><label>Proxy</label><select class="input" [(ngModel)]="form.proxyId"><option [ngValue]="null">None</option>@for (p of proxies(); track p.id) { <option [value]="p.id">{{ p.type }} {{ p.host }}:{{ p.port }}</option> }</select></div>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="showCreate.set(false)">Cancel</button><button class="btn btn-primary" (click)="create()">Create</button></div>
    </div></div>
  }
  @if (qrAccount(); as a) {
    <div class="modal-backdrop" (click)="qrAccount.set(null)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">Scan QR — {{ a.displayName }}</div>
      <div class="modal-body" style="text-align:center">
        <div class="qr">{{ qrData() || 'Waiting for QR…' }}</div>
        <p class="muted">Open WhatsApp → Linked devices → Link a device.</p>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="qrAccount.set(null)">Close</button></div>
    </div></div>
  }
  @if (antiBanAccount(); as a) {
    <div class="modal-backdrop" (click)="antiBanAccount.set(null)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">Anti-ban — {{ a.displayName }}</div>
      <div class="modal-body">
        <div class="flex gap"><div class="field" style="flex:1"><label>Min delay (s)</label><input class="input" type="number" [(ngModel)]="antiBan.minDelaySec" /></div><div class="field" style="flex:1"><label>Max delay (s)</label><input class="input" type="number" [(ngModel)]="antiBan.maxDelaySec" /></div></div>
        <div class="flex gap"><div class="field" style="flex:1"><label>Batch size</label><input class="input" type="number" [(ngModel)]="antiBan.batchSize" /></div><div class="field" style="flex:1"><label>Cooldown (m)</label><input class="input" type="number" [(ngModel)]="antiBan.cooldownMinutes" /></div></div>
        <div class="field"><label>Daily cap</label><input class="input" type="number" [(ngModel)]="antiBan.dailyCap" /></div>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="antiBanAccount.set(null)">Cancel</button><button class="btn btn-primary" (click)="saveAntiBan(a)">Save</button></div>
    </div></div>
  }
  `,
  styles: [`.qr { font-family:monospace; word-break:break-all; background:var(--panel-2); padding:1.5rem; border-radius:10px; margin-bottom:1rem; }`]
})
export class AccountsComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  items = signal<WaAccount[]>([]);
  proxies = signal<Proxy[]>([]);
  loading = signal(true);
  showCreate = signal(false);
  qrAccount = signal<WaAccount | null>(null);
  qrData = signal<string | null>(null);
  antiBanAccount = signal<WaAccount | null>(null);
  antiBan: AntiBan = { minDelaySec: 5, maxDelaySec: 15, batchSize: 50, cooldownMinutes: 5, dailyCap: 1000 };
  form: any = {};

  ngOnInit() { this.load(); }
  load() { this.loading.set(true); this.api.accounts().subscribe({ next: r => { this.items.set(r); this.loading.set(false); }, error: () => { this.loading.set(false); this.toast.error('API unreachable'); } }); }
  openCreate() { this.form = { displayName: '', engineType: 'Baileys', phoneNumber: '', proxyId: null }; this.api.proxies().subscribe(p => this.proxies.set(p)); this.showCreate.set(true); }
  create() { this.api.createAccount(this.form).subscribe({ next: () => { this.toast.success('Account added'); this.showCreate.set(false); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') }); }
  connect(a: WaAccount) { this.api.connectAccount(a.id).subscribe(() => { this.qrAccount.set(a); this.api.accountQr(a.id).subscribe(q => this.qrData.set(q.qrData || null)); this.load(); }); }
  disconnect(a: WaAccount) { this.api.disconnectAccount(a.id).subscribe(() => { this.toast.success('Disconnected'); this.load(); }); }
  remove(a: WaAccount) { if (confirm('Delete account?')) this.api.deleteAccount(a.id).subscribe(() => { this.toast.success('Deleted'); this.load(); }); }
  openAntiBan(a: WaAccount) { this.antiBanAccount.set(a); this.api.antiBan(a.id).subscribe(s => this.antiBan = s); }
  saveAntiBan(a: WaAccount) { this.api.updateAntiBan(a.id, this.antiBan).subscribe(() => { this.toast.success('Saved'); this.antiBanAccount.set(null); }); }
  pct(a: WaAccount) { return a.dailyLimit ? Math.min(100, Math.round(a.sentToday * 100 / a.dailyLimit)) : 0; }
  badge(s: string) { return { Connected: 'badge-green', QrReady: 'badge-amber', Connecting: 'badge-amber', Banned: 'badge-red', Error: 'badge-red' }[s] || 'badge-gray'; }
}
