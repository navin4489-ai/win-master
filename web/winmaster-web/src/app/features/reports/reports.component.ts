import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { DeliveryReport, KpiReport } from '../../core/models';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [DatePipe],
  template: `
  <div class="page-head"><h1>Reports</h1></div>
  @if (kpi(); as k) {
    <div class="stat-grid">
      <div class="card stat"><div class="l">Delivery Rate</div><div class="n">{{ k.deliveryRate }}%</div></div>
      <div class="card stat"><div class="l">Crash-Free</div><div class="n">{{ k.crashFreeSessions }}%</div></div>
      <div class="card stat"><div class="l">Suspension Rate</div><div class="n">{{ k.suspensionRate }}%</div></div>
      <div class="card stat"><div class="l">Total Messages</div><div class="n">{{ k.totalMessages }}</div></div>
      <div class="card stat"><div class="l">Active Accounts</div><div class="n">{{ k.activeAccounts }}</div></div>
      <div class="card stat"><div class="l">Active Licenses</div><div class="n">{{ k.activeLicenses }}</div></div>
    </div>
  }
  @if (report(); as r) {
    <div class="card card-pad" style="margin-top:1.3rem">
      <div class="between"><h3>Delivery (last 7 days)</h3><span class="muted">{{ r.sent }} sent · {{ r.delivered }} delivered · {{ r.failed }} failed</span></div>
      <div class="chart">
        @for (p of r.series; track p.date) {
          <div class="bar-col">
            <div class="bars">
              <div class="bar green" [style.height.%]="h(p.delivered, r)"></div>
              <div class="bar red" [style.height.%]="h(p.failed, r)"></div>
            </div>
            <div class="xl">{{ p.date | date:'MMM d' }}</div>
          </div>
        }
        @if (!r.series.length) { <div class="empty">No delivery data in range</div> }
      </div>
      <div class="legend muted"><span class="dot green"></span> Delivered <span class="dot red"></span> Failed</div>
    </div>
  }
  `,
  styles: [`
    .chart { display:flex; gap:.6rem; align-items:flex-end; height:220px; padding:1rem 0; }
    .bar-col { flex:1; display:flex; flex-direction:column; align-items:center; height:100%; justify-content:flex-end; }
    .bars { display:flex; gap:3px; align-items:flex-end; height:170px; }
    .bar { width:14px; border-radius:4px 4px 0 0; } .bar.green { background:var(--primary); } .bar.red { background:var(--danger); }
    .xl { font-size:.68rem; color:var(--muted); margin-top:.4rem; }
    .legend { margin-top:.5rem; font-size:.78rem; } .dot { display:inline-block; width:10px; height:10px; border-radius:50%; margin:0 .3rem 0 .8rem; }
    .dot.green { background:var(--primary); } .dot.red { background:var(--danger); }
  `]
})
export class ReportsComponent implements OnInit {
  private api = inject(ApiService); private toast = inject(ToastService);
  report = signal<DeliveryReport | null>(null);
  kpi = signal<KpiReport | null>(null);
  ngOnInit() {
    this.api.delivery({}).subscribe({ next: r => this.report.set(r), error: () => this.toast.error('API unreachable') });
    this.api.kpis({}).subscribe({ next: k => this.kpi.set(k) });
  }
  h(v: number, r: DeliveryReport) { const max = Math.max(1, ...r.series.map(s => s.delivered + s.failed)); return Math.round(v * 100 / max); }
}
