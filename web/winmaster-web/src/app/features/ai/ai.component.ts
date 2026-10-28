import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { AiSettings } from '../../core/models';

interface ProviderInfo { id: string; name: string; free: boolean; needsKey: boolean; models: string[]; signupUrl?: string; }

@Component({
  selector: 'app-ai',
  standalone: true,
  imports: [FormsModule],
  template: `
  <div class="page-head"><h1>AI Chatbot &amp; Message Generator</h1></div>
  <div class="grid" style="grid-template-columns:1fr 1fr; align-items:start; gap:1.3rem">
    <div class="card card-pad">
      <h3>Provider</h3>
      <p class="muted" style="margin-top:-.3rem;font-size:.82rem">Pick a free model. "Built-in" needs no key and works instantly.</p>
      <div class="field">
        <label>AI Provider</label>
        <select class="input" [(ngModel)]="form.provider" (change)="onProvider()">
          @for (p of providers(); track p.id) {
            <option [value]="p.id">{{ p.name }}{{ p.free ? ' · FREE' : '' }}</option>
          }
        </select>
      </div>
      @if (current(); as p) {
        <div class="field">
          <label>Model</label>
          <select class="input" [(ngModel)]="form.model">
            @for (m of p.models; track m) { <option [value]="m">{{ m }}</option> }
          </select>
        </div>
        @if (p.needsKey) {
          <div class="field">
            <label>API key @if (p.free) { <span class="badge badge-green">free key</span> }</label>
            <input class="input" type="password" [(ngModel)]="form.apiKey" [placeholder]="form.maskedApiKey || 'paste key'" />
            @if (p.signupUrl) { <a [href]="p.signupUrl" target="_blank" style="font-size:.78rem">Get a free key →</a> }
          </div>
        } @else {
          <div class="badge badge-blue" style="margin-bottom:.8rem">No API key required</div>
        }
      }
      <div class="field"><label>System prompt (chatbot personality)</label><textarea class="input" rows="3" [(ngModel)]="form.systemPrompt"></textarea></div>
      <label class="flex gap-sm" style="align-items:center;margin-bottom:1rem"><input type="checkbox" [(ngModel)]="form.isActive" /> Enable AI auto-replies in inbox</label>
      <button class="btn btn-primary" (click)="save()">Save</button>
    </div>

    <div class="grid" style="gap:1.3rem">
      <div class="card card-pad">
        <h3>Test chatbot</h3>
        <div class="field"><label>Incoming message</label><input class="input" [(ngModel)]="testMsg" (keyup.enter)="test()" /></div>
        <button class="btn btn-ghost" (click)="test()" [disabled]="busy()">{{ busy() ? 'Thinking…' : 'Get AI reply' }}</button>
        @if (reply()) { <div class="card card-pad" style="margin-top:1rem;background:var(--panel-2);white-space:pre-wrap">{{ reply() }}</div> }
      </div>

      <div class="card card-pad">
        <h3>Generate a campaign message</h3>
        <div class="field"><label>What is it about?</label><input class="input" [(ngModel)]="genPrompt" placeholder="e.g. 20% off weekend sale on sneakers" (keyup.enter)="generate()" /></div>
        <div class="flex gap">
          <div class="field" style="flex:1"><label>Tone</label><select class="input" [(ngModel)]="genTone"><option>Friendly</option><option>Professional</option><option>Urgent</option><option>Playful</option></select></div>
          <div class="field" style="flex:1"><label>Goal</label><select class="input" [(ngModel)]="genGoal"><option>Drive sales</option><option>Announce</option><option>Re-engage</option><option>Collect replies</option></select></div>
        </div>
        <div class="flex gap" style="margin-bottom:.8rem">
          <label class="flex gap-sm" style="align-items:center"><input type="checkbox" [(ngModel)]="genSpintax" /> Spintax</label>
          <label class="flex gap-sm" style="align-items:center"><input type="checkbox" [(ngModel)]="genVars" /> {{ '{{Name}}' }} variable</label>
        </div>
        <button class="btn btn-primary" (click)="generate()" [disabled]="busy()">{{ busy() ? 'Generating…' : '✨ Generate message' }}</button>
        @if (generated()) {
          <div class="card card-pad" style="margin-top:1rem;background:var(--panel-2);white-space:pre-wrap">{{ generated() }}</div>
          <button class="btn btn-ghost btn-sm" style="margin-top:.6rem" (click)="copy()">Copy</button>
        }
      </div>
    </div>
  </div>
  `
})
export class AiComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  providers = signal<ProviderInfo[]>([]);
  current = signal<ProviderInfo | null>(null);
  form: any = { provider: 'Local', model: 'local-generator', apiKey: '', systemPrompt: '', isActive: true };
  testMsg = 'Hi, what are your prices?';
  reply = signal('');
  genPrompt = ''; genTone = 'Friendly'; genGoal = 'Drive sales'; genSpintax = true; genVars = true;
  generated = signal('');
  busy = signal(false);

  ngOnInit() {
    this.api.aiProviders().subscribe({ next: (c: any) => { this.providers.set(c.providers); this.syncCurrent(); }, error: () => {} });
    this.api.aiSettings().subscribe({ next: s => { this.form = { ...s, apiKey: '' }; this.syncCurrent(); }, error: () => this.toast.error('API unreachable') });
  }
  onProvider() { this.syncCurrent(); const p = this.current(); if (p && !p.models.includes(this.form.model)) this.form.model = p.models[0]; }
  syncCurrent() { this.current.set(this.providers().find(p => p.id === this.form.provider) ?? null); }
  save() { this.api.updateAi(this.form).subscribe({ next: s => { this.form = { ...s, apiKey: '' }; this.syncCurrent(); this.toast.success('AI settings saved'); }, error: e => this.toast.error(e?.error?.detail || 'Failed') }); }
  test() {
    this.busy.set(true);
    this.api.testAi(this.testMsg).subscribe({ next: r => { this.reply.set(r.reply); this.busy.set(false); }, error: e => { this.busy.set(false); this.toast.error(e?.error?.detail || 'Failed'); } });
  }
  generate() {
    if (!this.genPrompt.trim()) { this.toast.error('Describe what the message is about'); return; }
    this.busy.set(true);
    this.api.aiGenerate({ prompt: this.genPrompt, tone: this.genTone, goal: this.genGoal, includeSpintax: this.genSpintax, includeVariables: this.genVars })
      .subscribe({ next: r => { this.generated.set(r.message); this.busy.set(false); }, error: e => { this.busy.set(false); this.toast.error(e?.error?.detail || 'Failed'); } });
  }
  copy() { navigator.clipboard.writeText(this.generated()); this.toast.success('Copied'); }
}
