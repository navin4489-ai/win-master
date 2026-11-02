import { Component, OnInit, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { Campaign, ContactGroup, Template, WaAccount } from '../../core/models';

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
            <td>
              <span class="badge" [class]="badge(c.status)">{{ c.status }}</span>
              @if (c.status === 'Paused' && c.pauseReason) {
                <div class="muted" style="font-size:11px;max-width:220px" [title]="c.connectionMessage || ''">
                  {{ pauseLabel(c) }}
                </div>
              }
            </td>
            <td style="min-width:150px"><div class="progress"><span [style.width.%]="c.progressPercent"></span></div><small class="muted">{{ c.progressPercent }}%</small></td>
            <td>{{ c.processedCount }}/{{ c.totalRecipients }}</td>
            <td class="row-actions">
              @if (c.status === 'Draft' || c.status === 'Scheduled' || c.status === 'Paused') { <button class="btn btn-primary btn-sm" (click)="action(c,'start')">Start</button> }
              @if (c.status !== 'Running') { <button class="btn btn-ghost btn-sm" (click)="openEdit(c)">Edit</button> }
              @if (c.status === 'Running') { <button class="btn btn-ghost btn-sm" (click)="action(c,'pause')">Pause</button> }
              @if (c.status === 'Running' || c.status === 'Paused') { <button class="btn btn-danger btn-sm" (click)="action(c,'stop')">Stop</button> }
              @if (c.status === 'Completed' || c.status === 'Stopped' || c.status === 'Failed') { <button class="btn btn-primary btn-sm" (click)="action(c,'restart')">Restart</button> }
              @if (c.status !== 'Running' && c.failedCount > 0) { <button class="btn btn-ghost btn-sm" (click)="retryFailed(c)" title="Re-send only to recipients that failed">Retry failed ({{ c.failedCount }})</button> }
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
        <div class="modal-head">{{ editId() ? 'Edit campaign' : 'Launch a campaign' }}</div>
        <div class="modal-body">
          <div class="field"><label>Name</label><input class="input" [(ngModel)]="form.name" /></div>
          <div class="field"><label>Contact group</label>
            <select class="input" [(ngModel)]="form.groupId">
              @for (g of groups(); track g.id) { <option [ngValue]="g.id">{{ g.name }} ({{ g.totalContacts }})</option> }
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
            <div class="field" style="flex:1"><label>Engine</label><select class="input" [(ngModel)]="form.engineType" (change)="onEngineChange()"><option>Baileys</option><option>CloudApi</option></select></div>
            <div class="field" style="flex:1"><label>Rotation</label><select class="input" [(ngModel)]="form.rotationMode"><option>RoundRobin</option><option>Sequential</option></select></div>
          </div>
          <div class="field">
            <label>Send from account</label>
            <select class="input" [(ngModel)]="form.accountId">
              <option [ngValue]="null">Auto (any {{ form.engineType }} account)</option>
              @for (a of accountsForEngine(); track a.id) {
                <option [ngValue]="a.id">{{ a.displayName }} {{ a.phoneNumber ? '· ' + a.phoneNumber : '' }}{{ a.isDefault ? ' (default)' : '' }}{{ a.isRestricted ? ' — restricted' : '' }}</option>
              }
            </select>
            @if (!accountsForEngine().length) { <small class="muted">No {{ form.engineType }} account yet — add one under Accounts.</small> }
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
          <button class="btn btn-primary" (click)="create()">{{ editId() ? 'Save changes' : 'Create' }}</button>
        </div>
      </div>
    </div>
  }
  `
})
export class CampaignsComponent implements OnInit, OnDestroy {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  items = signal<Campaign[]>([]);
  groups = signal<ContactGroup[]>([]);
  templates = signal<Template[]>([]);
  accounts = signal<WaAccount[]>([]);
  loading = signal(true);
  showCreate = signal(false);
  editId = signal<number | null>(null);
  aiBusy = signal(false);
  q = ''; status = '';
  form: any = this.blank();
  private pollTimer: any = null;

  ngOnInit() { this.load(); this.startPolling(); }
  ngOnDestroy() { this.stopPolling(); }

  // Silently refresh the list every 5s while any campaign is actively progressing,
  // so statuses/progress update without a manual reload.
  private startPolling() {
    this.stopPolling();
    this.pollTimer = setInterval(() => {
      if (this.showCreate()) return; // don't refresh under the open modal
      const active = this.items().some(c => c.status === 'Running' || c.status === 'Scheduled' || c.status === 'Paused');
      if (active) this.refresh();
    }, 5000);
  }
  private stopPolling() { if (this.pollTimer) { clearInterval(this.pollTimer); this.pollTimer = null; } }

  // Refresh without toggling the loading spinner (avoids flicker during polling).
  private refresh() {
    this.api.campaigns({ q: this.q, status: this.status, pageSize: 100 }).subscribe({
      next: r => this.items.set(r.items),
      error: () => {}
    });
  }

  load() {
    this.loading.set(true);
    this.api.campaigns({ q: this.q, status: this.status, pageSize: 100 }).subscribe({
      next: r => { this.items.set(r.items); this.loading.set(false); },
      error: () => { this.loading.set(false); this.toast.error('API unreachable'); }
    });
  }
  openCreate() {
    this.editId.set(null);
    this.form = this.blank();
    this.api.groups({ pageSize: 100 }).subscribe(r => { this.groups.set(r.items); if (r.items[0]) this.form.groupId = r.items[0].id; });
    this.api.templates().subscribe(r => this.templates.set(r));
    this.api.accounts().subscribe(r => { this.accounts.set(r); this.preselectAccount(); });
    this.showCreate.set(true);
  }
  // Opens the same modal pre-filled to edit an existing campaign. Editing a finished
  // (Completed/Stopped/Failed) campaign resets it to a clean Draft on save — warn first.
  openEdit(c: Campaign) {
    const finished = c.status === 'Completed' || c.status === 'Stopped' || c.status === 'Failed';
    if (finished && !confirm('Editing this campaign will reset it to Draft and clear its past send history, so you can edit and send it again. Continue?')) return;
    this.editId.set(c.id);
    this.form = {
      name: c.name, groupId: c.groupId, templateId: c.templateId ?? null, body: c.body ?? '',
      engineType: c.engineType, accountId: c.accountId ?? null, rotationMode: c.rotationMode,
      scheduledAt: c.scheduledAt ? this.toLocalInput(c.scheduledAt) : '',
      spintaxEnabled: c.spintaxEnabled, personalizeMessage: c.personalizeMessage,
      pacing: { ...c.pacing },
    };
    this.api.groups({ pageSize: 100 }).subscribe(r => this.groups.set(r.items));
    this.api.templates().subscribe(r => this.templates.set(r));
    this.api.accounts().subscribe(r => this.accounts.set(r));
    this.showCreate.set(true);
  }
  // Converts a UTC ISO string to the local value a datetime-local input expects.
  private toLocalInput(iso: string): string {
    const d = new Date(iso); if (isNaN(d.getTime())) return '';
    const pad = (n: number) => `${n}`.padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }
  // Accounts matching the currently selected engine (can't send from a CloudApi number on a Baileys campaign).
  accountsForEngine(): WaAccount[] {
    return this.accounts().filter(a => a.engineType === this.form.engineType);
  }
  // Pre-select the user's default account for this engine, if any.
  preselectAccount() {
    const list = this.accountsForEngine();
    const def = list.find(a => a.isDefault && !a.isRestricted) || list.find(a => !a.isRestricted) || list[0];
    this.form.accountId = def ? def.id : null;
  }
  onEngineChange() { this.preselectAccount(); }
  create() {
    const name = (this.form.name || '').trim();
    const groupId = +this.form.groupId;
    // Validate loudly so the form never silently does nothing.
    if (!name) { this.toast.error('Campaign name is required'); return; }
    if (!groupId) { this.toast.error('Please select a contact group'); return; }
    if (!this.form.templateId && !(this.form.body || '').trim()) { this.toast.error('Add a message or choose a template'); return; }
    const b = {
      ...this.form,
      name,
      groupId,
      templateId: this.form.templateId ? +this.form.templateId : null,
      // datetime-local is naive local time; convert to a real UTC ISO string so
      // the server (which schedules in UTC) fires at the user's intended moment.
      scheduledAt: this.form.scheduledAt ? new Date(this.form.scheduledAt).toISOString() : null,
    };
    const id = this.editId();
    if (id) {
      this.api.updateCampaign(id, b).subscribe({
        next: () => { this.toast.success('Campaign updated'); this.showCreate.set(false); this.editId.set(null); this.load(); },
        error: e => this.toast.error(e?.error?.detail || 'Failed to update')
      });
    } else {
      this.api.createCampaign(b).subscribe({
        next: () => { this.toast.success('Campaign created'); this.showCreate.set(false); this.load(); },
        error: e => this.toast.error(e?.error?.detail || 'Failed to create')
      });
    }
  }
  action(c: Campaign, a: string) {
    this.api.campaignAction(c.id, a).subscribe({ next: () => { this.toast.success(`Campaign ${a}ed`); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') });
  }
  retryFailed(c: Campaign) {
    this.api.campaignRetryFailed(c.id).subscribe({ next: () => { this.toast.success('Retrying failed recipients'); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed to retry') });
  }
  badge(s: string) { return { Running: 'badge-blue', Completed: 'badge-green', Scheduled: 'badge-amber', Failed: 'badge-red', Paused: 'badge-amber' }[s] || 'badge-gray'; }
  /** Short human label for why the system auto-paused a campaign. */
  pauseLabel(c: Campaign): string {
    if (c.pauseReason === 'daily_limit') {
      const at = c.limitResumeAt ? new Date(c.limitResumeAt) : null;
      return at && !isNaN(at.getTime()) ? `Daily safe limit — resumes ${at.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}` : 'Daily safe limit reached';
    }
    if (c.pauseReason === 'restriction') return 'WhatsApp restriction — protecting account';
    if (c.pauseReason === 'failures') return 'Paused — check contact numbers';
    if (c.pauseReason === 'connection') return 'WhatsApp disconnected';
    return c.connectionMessage || 'Paused';
  }
  aiGenerate() {
    if (!this.form.name && !this.form.body) { this.toast.error('Enter a campaign name or topic first'); return; }
    this.aiBusy.set(true);
    const prompt = this.form.body?.trim() || this.form.name;
    this.api.aiGenerate({ prompt, tone: 'Friendly', goal: 'Drive sales', includeSpintax: this.form.spintaxEnabled, includeVariables: true })
      .subscribe({ next: (r: any) => { this.form.body = r.message; this.aiBusy.set(false); this.toast.success('AI message generated'); },
        error: (e: any) => { this.aiBusy.set(false); this.toast.error(e?.error?.detail || 'AI failed'); } });
  }
  blank() { return { name: '', groupId: null, templateId: null, body: '', engineType: 'Baileys', accountId: null, rotationMode: 'RoundRobin', scheduledAt: '', spintaxEnabled: true, personalizeMessage: true, pacing: { minDelaySec: 30, maxDelaySec: 60, batchSize: 15, cooldownMinutes: 10 } }; }
}
