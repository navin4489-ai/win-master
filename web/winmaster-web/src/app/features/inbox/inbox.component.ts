import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { InboxThread, InboxMessage } from '../../core/models';

@Component({
  selector: 'app-inbox',
  standalone: true,
  imports: [FormsModule, DatePipe],
  template: `
  <div class="page-head"><h1>Inbox</h1></div>
  <div class="split card">
    <div class="threads">
      <div class="card-pad"><input class="input" placeholder="Search…" [(ngModel)]="q" (keyup.enter)="loadThreads()" /></div>
      @for (t of threads(); track t.id) {
        <div class="thread" [class.active]="active()?.id === t.id" (click)="open(t)">
          <div class="between"><b>{{ t.contactName || t.contactPhoneMasked }}</b>@if (t.unreadCount) { <span class="badge badge-green">{{ t.unreadCount }}</span> }</div>
          <div class="muted" style="font-size:.8rem">{{ t.preview }}</div>
          <div class="muted" style="font-size:.72rem">{{ t.accountLabel }}</div>
        </div>
      }
      @if (!threads().length) { <div class="empty">No conversations</div> }
    </div>
    <div class="chat">
      @if (active(); as a) {
        <div class="chat-head between">
          <div><b>{{ a.contactName || a.contactPhoneMasked }}</b><div class="muted" style="font-size:.78rem">{{ a.accountLabel }}</div></div>
          <label class="flex gap-sm" style="align-items:center;font-size:.8rem"><input type="checkbox" [checked]="a.isBotEnabled" (change)="toggleBot(a)" /> Bot</label>
        </div>
        <div class="messages">
          @for (m of messages(); track m.id) {
            <div class="msg" [class.out]="m.direction === 'Out'">
              <div class="bubble">{{ m.body }}<div class="meta">{{ m.sentAt | date:'short' }} @if (m.status) { · {{ m.status }} }</div></div>
            </div>
          }
        </div>
        <div class="composer">
          <input class="input" placeholder="Type a reply…" [(ngModel)]="draft" (keyup.enter)="send()" />
          <button class="btn btn-primary" (click)="send()">Send</button>
        </div>
      } @else { <div class="empty" style="margin:auto">Select a conversation</div> }
    </div>
  </div>
  `,
  styles: [`
    .split { display:grid; grid-template-columns: 320px 1fr; height: calc(100vh - 170px); overflow:hidden; }
    .threads { border-right:1px solid var(--border); overflow:auto; }
    .thread { padding:.7rem 1rem; border-bottom:1px solid var(--border); cursor:pointer; }
    .thread:hover, .thread.active { background: var(--panel-2); }
    .chat { display:flex; flex-direction:column; }
    .chat-head { padding:.8rem 1.1rem; border-bottom:1px solid var(--border); }
    .messages { flex:1; overflow:auto; padding:1rem; background:#f1f5f9; display:flex; flex-direction:column; gap:.5rem; }
    .msg { display:flex; } .msg.out { justify-content:flex-end; }
    .bubble { background:#fff; padding:.5rem .75rem; border-radius:12px; max-width:70%; box-shadow:var(--shadow); font-size:.88rem; }
    .msg.out .bubble { background:#dcf8c6; }
    .meta { font-size:.68rem; color:var(--muted); margin-top:.25rem; }
    .composer { display:flex; gap:.5rem; padding:.8rem; border-top:1px solid var(--border); }
  `]
})
export class InboxComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  threads = signal<InboxThread[]>([]);
  messages = signal<InboxMessage[]>([]);
  active = signal<InboxThread | null>(null);
  q = ''; draft = '';

  ngOnInit() { this.loadThreads(); }
  loadThreads() { this.api.threads({ q: this.q, pageSize: 100 }).subscribe({ next: r => this.threads.set(r.items), error: () => this.toast.error('API unreachable') }); }
  open(t: InboxThread) {
    this.active.set(t);
    this.api.messages(t.id, { pageSize: 200 }).subscribe(r => this.messages.set(r.items));
    if (t.unreadCount) this.api.markRead(t.id).subscribe(() => { t.unreadCount = 0; });
  }
  send() {
    const a = this.active(); if (!a || !this.draft.trim()) return;
    this.api.sendMessage(a.id, { body: this.draft }).subscribe({ next: m => { this.messages.update(l => [...l, m]); this.draft = ''; }, error: e => this.toast.error(e?.error?.detail || 'Failed') });
  }
  toggleBot(a: InboxThread) { this.api.toggleBot(a.id, !a.isBotEnabled).subscribe(() => { a.isBotEnabled = !a.isBotEnabled; }); }
}
