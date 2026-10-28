import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { Proxy } from '../../core/models';

@Component({
  selector: 'app-proxies',
  standalone: true,
  imports: [FormsModule],
  template: `
  <div class="page-head"><h1>Proxies</h1><button class="btn btn-primary" (click)="open()">+ Add Proxy</button></div>
  <div class="card">
    <table class="table">
      <thead><tr><th>Type</th><th>Host</th><th>Port</th><th>User</th><th>Health</th><th></th></tr></thead>
      <tbody>
        @for (p of items(); track p.id) {
          <tr><td><span class="badge badge-gray">{{ p.type }}</span></td><td>{{ p.host }}</td><td>{{ p.port }}</td><td>{{ p.username || '—' }}</td>
          <td>@if (p.isHealthy) { <span class="badge badge-green">Healthy</span> } @else { <span class="badge badge-red">Down</span> }</td>
          <td class="row-actions"><button class="btn btn-ghost btn-sm" (click)="test(p)">Test</button><button class="btn btn-danger btn-sm" (click)="del(p)">Delete</button></td></tr>
        }
        @if (!items().length && !loading()) { <tr><td colspan="6" class="empty">No proxies</td></tr> }
      </tbody>
    </table>
  </div>
  @if (show()) {
    <div class="modal-backdrop" (click)="show.set(false)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">Add proxy</div>
      <div class="modal-body">
        <div class="field"><label>Type</label><select class="input" [(ngModel)]="form.type"><option>HTTP</option><option>HTTPS</option><option>SOCKS5</option></select></div>
        <div class="flex gap"><div class="field" style="flex:2"><label>Host</label><input class="input" [(ngModel)]="form.host" /></div><div class="field" style="flex:1"><label>Port</label><input class="input" type="number" [(ngModel)]="form.port" /></div></div>
        <div class="flex gap"><div class="field" style="flex:1"><label>Username</label><input class="input" [(ngModel)]="form.username" /></div><div class="field" style="flex:1"><label>Password</label><input class="input" type="password" [(ngModel)]="form.password" /></div></div>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="show.set(false)">Cancel</button><button class="btn btn-primary" (click)="create()">Create</button></div>
    </div></div>
  }
  `
})
export class ProxiesComponent implements OnInit {
  private api = inject(ApiService); private toast = inject(ToastService);
  items = signal<Proxy[]>([]); loading = signal(true); show = signal(false);
  form: any = {};
  ngOnInit() { this.load(); }
  load() { this.loading.set(true); this.api.proxies().subscribe({ next: r => { this.items.set(r); this.loading.set(false); }, error: () => { this.loading.set(false); this.toast.error('API unreachable'); } }); }
  open() { this.form = { type: 'SOCKS5', host: '', port: 1080, username: '', password: '' }; this.show.set(true); }
  create() { this.api.createProxy(this.form).subscribe({ next: () => { this.toast.success('Proxy added'); this.show.set(false); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') }); }
  test(p: Proxy) { this.api.testProxy(p.id).subscribe(r => r.ok ? this.toast.success(`OK ${r.latencyMs}ms`) : this.toast.error(r.error || 'Failed')); }
  del(p: Proxy) { this.api.deleteProxy(p.id).subscribe(() => { this.toast.success('Deleted'); this.load(); }); }
}
