import { Component, OnInit, inject, signal } from '@angular/core';
import { JsonPipe, DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { AuditLog } from '../../core/models';

@Component({
  selector: 'app-settings',
  standalone: true,
  template: `
  <div class="page-head"><h1>Settings &amp; Audit</h1></div>
  <div class="card card-pad" style="margin-bottom:1.3rem">
    <h3>Application settings</h3>
    <pre class="pre">{{ settings() | json }}</pre>
  </div>
  <div class="card">
    <div class="card-pad"><h3>Audit log</h3></div>
    <table class="table">
      <thead><tr><th>When</th><th>User</th><th>Action</th><th>Entity</th><th>Detail</th></tr></thead>
      <tbody>
        @for (a of logs(); track a.id) {
          <tr><td class="muted">{{ a.createdAt | date:'short' }}</td><td>{{ a.userId }}</td><td><span class="badge badge-gray">{{ a.action }}</span></td><td>{{ a.entity }}</td><td class="muted">{{ a.maskedDetail }}</td></tr>
        }
        @if (!logs().length) { <tr><td colspan="5" class="empty">No audit entries</td></tr> }
      </tbody>
    </table>
  </div>
  `,
  styles: [`.pre { background:var(--panel-2); padding:1rem; border-radius:10px; font-size:.8rem; overflow:auto; }`],
  imports: [JsonPipe, DatePipe],
})
export class SettingsComponent implements OnInit {
  private api = inject(ApiService);
  settings = signal<any>({});
  logs = signal<AuditLog[]>([]);
  ngOnInit() {
    this.api.settings().subscribe({ next: s => this.settings.set(s), error: () => {} });
    this.api.auditLogs({ pageSize: 50 }).subscribe({ next: r => this.logs.set(r.items), error: () => {} });
  }
}
