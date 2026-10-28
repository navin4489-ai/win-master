import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { AutoReplyRule, WaAccount } from '../../core/models';

@Component({
  selector: 'app-rules',
  standalone: true,
  imports: [FormsModule],
  template: `
  <div class="page-head"><h1>Auto-Reply Rules</h1><button class="btn btn-primary" (click)="open()">+ New Rule</button></div>
  <div class="card">
    <table class="table">
      <thead><tr><th>Priority</th><th>Match</th><th>Pattern</th><th>Response</th><th>AI</th><th>Active</th><th></th></tr></thead>
      <tbody>
        @for (r of items(); track r.id) {
          <tr><td>{{ r.priority }}</td><td><span class="badge badge-gray">{{ r.matchType }}</span></td><td><code>{{ r.pattern }}</code></td><td class="muted">{{ r.responseText }}</td>
          <td>{{ r.useAi ? 'Yes' : '—' }}</td><td><span class="badge" [class]="r.isActive ? 'badge-green' : 'badge-gray'">{{ r.isActive ? 'On' : 'Off' }}</span></td>
          <td class="row-actions"><button class="btn btn-ghost btn-sm" (click)="toggle(r)">Toggle</button><button class="btn btn-danger btn-sm" (click)="del(r)">Delete</button></td></tr>
        }
        @if (!items().length && !loading()) { <tr><td colspan="7" class="empty">No rules</td></tr> }
      </tbody>
    </table>
  </div>
  @if (show()) {
    <div class="modal-backdrop" (click)="show.set(false)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">New auto-reply rule</div>
      <div class="modal-body">
        <div class="field"><label>Account</label><select class="input" [(ngModel)]="form.accountId">@for (a of accounts(); track a.id) { <option [value]="a.id">{{ a.displayName }}</option> }</select></div>
        <div class="flex gap"><div class="field" style="flex:1"><label>Match type</label><select class="input" [(ngModel)]="form.matchType"><option>Exact</option><option>Wildcard</option><option>Regex</option></select></div><div class="field" style="flex:1"><label>Priority</label><input class="input" type="number" [(ngModel)]="form.priority" /></div></div>
        <div class="field"><label>Pattern</label><input class="input" [(ngModel)]="form.pattern" placeholder="hi / *price* / ^stop$" /></div>
        <div class="field"><label>Response text</label><textarea class="input" rows="2" [(ngModel)]="form.responseText"></textarea></div>
        <label class="flex gap-sm" style="align-items:center"><input type="checkbox" [(ngModel)]="form.useAi" /> Use AI response</label>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="show.set(false)">Cancel</button><button class="btn btn-primary" (click)="create()">Create</button></div>
    </div></div>
  }
  `
})
export class RulesComponent implements OnInit {
  private api = inject(ApiService); private toast = inject(ToastService);
  items = signal<AutoReplyRule[]>([]); accounts = signal<WaAccount[]>([]); loading = signal(true); show = signal(false);
  form: any = {};
  ngOnInit() { this.load(); this.api.accounts().subscribe(a => this.accounts.set(a)); }
  load() { this.loading.set(true); this.api.rules().subscribe({ next: r => { this.items.set(r); this.loading.set(false); }, error: () => { this.loading.set(false); this.toast.error('API unreachable'); } }); }
  open() { this.form = { accountId: this.accounts()[0]?.id, matchType: 'Exact', priority: 1, pattern: '', responseText: '', useAi: false, isActive: true }; this.show.set(true); }
  create() { this.api.createRule({ ...this.form, accountId: +this.form.accountId }).subscribe({ next: () => { this.toast.success('Rule created'); this.show.set(false); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') }); }
  toggle(r: AutoReplyRule) { this.api.toggleRule(r.id).subscribe(() => this.load()); }
  del(r: AutoReplyRule) { this.api.deleteRule(r.id).subscribe(() => { this.toast.success('Deleted'); this.load(); }); }
}
