import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { Campaign, ContactGroup, Template } from '../../core/models';

@Component({
  selector: 'app-campaigns',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
  <div class="page-head">
    <h1>Campaigns</h1>
    <button class="btn btn-primary" (click)="openCreate()">+ New Campaign</button>
  </div>

  <div class="card card-pad" style="margin-bottom:1rem">
    <div class="flex gap">
      <input class="input" placeholder="Search…" [(ngModel)]="q" (keyup.enter)="load()" style="max-width:280px" />
      <select class="input" [(ngModel)]="status" (change)="load()" style="max-width:180px">
        <option value="">All statuses</option>
        <option>Draft</option><option>Scheduled</option><option>Running</option><option>Paused</option><option>Completed</option><option>Stopped</option>
      </select>
    </div>
  </div>

  <div class="card">
    <table class="table">
      <thead><tr><th>Name</th><th>Engine</th><th>Status</th><th>Progress</th><th>Recipients</th><th>Actions</th></tr></thead>
      <tbody>
        @for (c of items(); track c.id) {
          <tr>
            <td><a [routerLink]="['/campaigns', c.id]"><b>{{ c.name }}</b></a><div class="muted">{{ c.groupName }}</div></td>
            <td><span class="badge badge-gray">{{ c.engineType }}</span></td>
            <td><span class="badge" [class]="badge(c.status)">{{ c.status }}</span></td>
            <td style="min-width:150px"><div class="progress"><span [style.width.%]="c.progressPercent"></span></div><small class="muted">{{ c.progressPercent }}%</small></td>
            <td>{{ c.processedCount }}/{{ c.totalRecipients }}</td>
            <td class="row-actions">
              @if (c.status === 'Draft' || c.status === 'Scheduled' || c.status === 'Paused') { <button class="btn btn-primary btn-sm" (click)="action(c,'start')">Start</button> }
              @if (c.status === 'Running') { <button class="btn btn-ghost btn-sm" (click)="action(c,'pause')">Pause</button> }
              @if (c.status === 'Running' || c.status === 'Paused') { <button class="btn btn-danger btn-sm" (click)="action(c,'stop')">Stop</button> }
            </td>
          </tr>
        }
        @if (!items().length && !loading()) { <tr><td colspan="6" class="empty">No campaigns</td></tr> }
      </tbody>
    </table>
  </div>

  @if (showCreate()) {
    <div class="modal-backdrop" (click)="showCreate.set(false)">
      <div class="modal" (click)="$event.stopPropagation()">
        <div class="modal-head">Launch a campaign</div>
        <div class="modal-body">
          <div class="field"><label>Name</label><input class="input" [(ngModel)]="form.name" /></div>
          <div class="field"><label>Contact group</label>
            <select class="input" [(ngModel)]="form.groupId">
              @for (g of groups(); track g.id) { <option [value]="g.id">{{ g.name }} ({{ g.totalContacts }})</option> }
            </select>
          </div>
          <div class="field"><label>Template</label>
            <select class="input" [(ngModel)]="form.templateId">
              <option [ngValue]="null">— inline message —</option>
              @for (t of templates(); track t.id) { <option [value]="t.id">{{ t.name }}</option> }
            </select>
          </div>
          @if (!form.templateId) {
            <div class="field">
              <label class="between">Message <button class="btn btn-ghost btn-sm" type="button" (click)="aiGenerate()" [disabled]="aiBusy()">{{ aiBusy() ? '…' : '✨ AI generate' }}</button></label>
              <textarea class="input" rows="3" [(ngModel)]="form.body" placeholder="{{ '{Hi|Hello}' }} {{ '{{Name}}' }}, ..."></textarea>
            </div>
          }
          <div class="flex gap">
            <div class="field" style="flex:1"><label>Engine</label><select class="input" [(ngModel)]="form.engineType"><option>Baileys</option><option>CloudApi</option></select></div>
            <div class="field" style="flex:1"><label>Rotation</label><select class="input" [(ngModel)]="form.rotationMode"><option>RoundRobin</option><option>Sequential</option></select></div>
          </div>
          <div class="flex gap">
            <div class="field" style="flex:1"><label>Min delay (s)</label><input class="input" type="number" [(ngModel)]="form.pacing.minDelaySec" /></div>
            <div class="field" style="flex:1"><label>Max delay (s)</label><input class="input" type="number" [(ngModel)]="form.pacing.maxDelaySec" /></div>
            <div class="field" style="flex:1"><label>Batch size</label><input class="input" type="number" [(ngModel)]="form.pacing.batchSize" /></div>
            <div class="field" style="flex:1"><label>Cooldown (m)</label><input class="input" type="number" [(ngModel)]="form.pacing.cooldownMinutes" /></div>
          </div>
          <div class="field"><label>Schedule (optional)</label><input class="input" type="datetime-local" [(ngModel)]="form.scheduledAt" /></div>
          <label class="flex gap-sm" style="align-items:center"><input type="checkbox" [(ngModel)]="form.spintaxEnabled" /> Spintax variation</label>
        </div>
        <div class="modal-foot">
          <button class="btn btn-ghost" (click)="showCreate.set(false)">Cancel</button>
          <button class="btn btn-primary" (click)="create()">Create</button>
        </div>
      </div>
    </div>
  }
  `
})
export class CampaignsComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  items = signal<Campaign[]>([]);
  groups = signal<ContactGroup[]>([]);
  templates = signal<Template[]>([]);
  loading = signal(true);
  showCreate = signal(false);
  aiBusy = signal(false);
  q = ''; status = '';
  form: any = this.blank();

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    this.api.campaigns({ q: this.q, status: this.status, pageSize: 100 }).subscribe({
      next: r => { this.items.set(r.items); this.loading.set(false); },
      error: () => { this.loading.set(false); this.toast.error('API unreachable'); }
    });
  }
  openCreate() {
    this.form = this.blank();
    this.api.groups({ pageSize: 100 }).subscribe(r => { this.groups.set(r.items); if (r.items[0]) this.form.groupId = r.items[0].id; });
    this.api.templates().subscribe(r => this.templates.set(r));
    this.showCreate.set(true);
  }
  create() {
    const b = {
      ...this.form,
      groupId: +this.form.groupId,
      templateId: this.form.templateId ? +this.form.templateId : null,
      // Empty datetime-local sends ''; the API needs null for the nullable DateTime.
      scheduledAt: this.form.scheduledAt ? this.form.scheduledAt : null,
    };
    this.api.createCampaign(b).subscribe({
      next: () => { this.toast.success('Campaign created'); this.showCreate.set(false); this.load(); },
      error: e => this.toast.error(e?.error?.detail || 'Failed to create')
    });
  }
  action(c: Campaign, a: string) {
    this.api.campaignAction(c.id, a).subscribe({ next: () => { this.toast.success(`Campaign ${a}ed`); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') });
  }
  badge(s: string) { return { Running: 'badge-blue', Completed: 'badge-green', Scheduled: 'badge-amber', Failed: 'badge-red', Paused: 'badge-amber' }[s] || 'badge-gray'; }
  aiGenerate() {
    if (!this.form.name && !this.form.body) { this.toast.error('Enter a campaign name or topic first'); return; }
    this.aiBusy.set(true);
    const prompt = this.form.body?.trim() || this.form.name;
    this.api.aiGenerate({ prompt, tone: 'Friendly', goal: 'Drive sales', includeSpintax: this.form.spintaxEnabled, includeVariables: true })
      .subscribe({ next: (r: any) => { this.form.body = r.message; this.aiBusy.set(false); this.toast.success('AI message generated'); },
        error: (e: any) => { this.aiBusy.set(false); this.toast.error(e?.error?.detail || 'AI failed'); } });
  }
  blank() { return { name: '', groupId: null, templateId: null, body: '', engineType: 'Baileys', rotationMode: 'RoundRobin', scheduledAt: '', spintaxEnabled: true, personalizeMessage: true, pacing: { minDelaySec: 5, maxDelaySec: 15, batchSize: 50, cooldownMinutes: 5 } }; }
}
