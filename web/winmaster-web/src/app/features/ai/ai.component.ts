import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { AiSettings } from '../../core/models';

@Component({
  selector: 'app-ai',
  standalone: true,
  imports: [FormsModule],
  template: `
  <div class="page-head"><h1>AI Chatbot</h1></div>
  <div class="grid" style="grid-template-columns:1fr 1fr; align-items:start">
    <div class="card card-pad">
      <h3>Provider settings</h3>
      <div class="field"><label>Provider</label><select class="input" [(ngModel)]="form.provider"><option [ngValue]="null">Disabled</option><option>OpenAI</option><option>Anthropic</option></select></div>
      <div class="field"><label>API key</label><input class="input" type="password" [(ngModel)]="form.apiKey" [placeholder]="form.maskedApiKey || 'sk-...'" /></div>
      <div class="field"><label>Model</label><input class="input" [(ngModel)]="form.model" /></div>
      <div class="field"><label>System prompt</label><textarea class="input" rows="4" [(ngModel)]="form.systemPrompt"></textarea></div>
      <div class="field"><label>Daily token budget</label><input class="input" type="number" [(ngModel)]="form.dailyTokenBudget" /></div>
      <label class="flex gap-sm" style="align-items:center;margin-bottom:1rem"><input type="checkbox" [(ngModel)]="form.isActive" /> Active</label>
      <button class="btn btn-primary" (click)="save()">Save</button>
    </div>
    <div class="card card-pad">
      <h3>Test</h3>
      <div class="field"><label>Message</label><input class="input" [(ngModel)]="testMsg" (keyup.enter)="test()" /></div>
      <button class="btn btn-ghost" (click)="test()">Send test</button>
      @if (reply()) { <div class="card card-pad" style="margin-top:1rem;background:var(--panel-2)">{{ reply() }}</div> }
    </div>
  </div>
  `
})
export class AiComponent implements OnInit {
  private api = inject(ApiService); private toast = inject(ToastService);
  form: any = { provider: null, model: 'gpt-4o-mini', systemPrompt: '', isActive: false, dailyTokenBudget: 100000 };
  testMsg = 'Hello, what are your prices?';
  reply = signal('');
  ngOnInit() { this.api.aiSettings().subscribe({ next: s => this.form = { ...s, apiKey: '' }, error: () => this.toast.error('API unreachable') }); }
  save() { this.api.updateAi(this.form).subscribe({ next: s => { this.form = { ...s, apiKey: '' }; this.toast.success('Saved'); }, error: e => this.toast.error(e?.error?.detail || 'Failed') }); }
  test() { this.api.testAi(this.testMsg).subscribe({ next: r => this.reply.set(r.reply), error: e => this.toast.error(e?.error?.detail || 'Failed') }); }
}
