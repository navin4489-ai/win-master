import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { Contact } from '../../core/models';

@Component({
  selector: 'app-group-detail',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
  <a routerLink="/contacts" class="muted">← Contacts</a>
  <div class="page-head" style="margin-top:.6rem">
    <h1>Group contacts</h1>
    <div class="row-actions">
      <button class="btn btn-primary" (click)="openAdd()">+ Add contact</button>
      <label class="btn btn-ghost" style="cursor:pointer" [class.disabled]="importing()">
        @if (importing()) { <span class="spinner"></span> Importing… } @else { Import CSV/XLSX }
        <input type="file" hidden accept=".csv,.xlsx" [disabled]="importing()" (change)="onFile($event)" />
      </label>
      <button class="btn btn-ghost" (click)="validateVisible()">Validate numbers</button>
    </div>
  </div>

  <div class="card card-pad" style="margin-bottom:1rem">
    <div class="flex gap">
      <input class="input" placeholder="Search…" [(ngModel)]="q" (keyup.enter)="load()" style="max-width:260px" />
      <select class="input" [(ngModel)]="valid" (change)="load()" style="max-width:180px">
        <option [ngValue]="undefined">All</option><option [ngValue]="true">On WhatsApp</option><option [ngValue]="false">Not registered</option>
      </select>
    </div>
  </div>

  <div class="card">
    <table class="table">
      <thead><tr><th>Phone</th><th>Name</th><th>On WhatsApp</th><th>Tags</th><th></th></tr></thead>
      <tbody>
        @for (c of items(); track c.id) {
          <tr>
            <td>{{ c.phone }}</td><td>{{ c.name }}</td>
            <td>@if (c.waRegistered === true) { <span class="badge badge-green">Yes</span> } @else if (c.waRegistered === false) { <span class="badge badge-red">No</span> } @else { <span class="badge badge-gray">?</span> }</td>
            <td>@for (t of c.tags; track t) { <span class="badge badge-blue" style="margin-right:3px">{{ t }}</span> }</td>
            <td class="row-actions"><button class="btn btn-primary btn-sm" (click)="openSend(c)">Send</button><button class="btn btn-danger btn-sm" (click)="del(c)">Delete</button></td>
          </tr>
        }
        @if (!items().length && !loading()) { <tr><td colspan="5" class="empty">No contacts</td></tr> }
      </tbody>
    </table>
    <div class="card-pad between">
      <span class="muted">{{ total() }} contacts</span>
      <div class="row-actions">
        <button class="btn btn-ghost btn-sm" [disabled]="page()<=1" (click)="go(-1)">Prev</button>
        <span>Page {{ page() }}</span>
        <button class="btn btn-ghost btn-sm" [disabled]="page()*pageSize>=total()" (click)="go(1)">Next</button>
      </div>
    </div>
  </div>

  @if (sendTo(); as c) {
    <div class="modal-backdrop" (click)="sendTo.set(null)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">Send WhatsApp message</div>
      <div class="modal-body">
        <p class="muted" style="margin-top:0">To <b>{{ c.name || c.phone }}</b> ({{ c.phone }}) from your connected account.</p>
        <div class="field"><label>Message</label><textarea class="input" rows="3" [(ngModel)]="sendBody" placeholder="Type your message…"></textarea></div>
      </div>
      <div class="modal-foot">
        <button class="btn btn-ghost" (click)="sendTo.set(null)">Cancel</button>
        <button class="btn btn-primary" (click)="doSend()" [disabled]="sending()">{{ sending() ? 'Sending…' : 'Send' }}</button>
      </div>
    </div></div>
  }

  @if (showAdd()) {
    <div class="modal-backdrop" (click)="showAdd.set(false)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">Add contact to group</div>
      <div class="modal-body">
        <div class="field"><label>Phone number</label><input class="input" [(ngModel)]="addForm.phone" placeholder="+15551234567" /></div>
        <div class="field"><label>Name</label><input class="input" [(ngModel)]="addForm.name" placeholder="Contact name" /></div>
        <div class="field"><label>Tags (comma separated)</label><input class="input" [(ngModel)]="addForm.tags" placeholder="lead,priority" /></div>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="showAdd.set(false)">Cancel</button><button class="btn btn-primary" (click)="addContact()" [disabled]="adding()">{{ adding() ? 'Adding…' : 'Add contact' }}</button></div>
    </div></div>
  }
  `,
  styles: [`
    .spinner { display:inline-block; width:14px; height:14px; margin-right:6px; vertical-align:-2px;
      border:2px solid rgba(255,255,255,.35); border-top-color:currentColor; border-radius:50%;
      animation:spin .6s linear infinite; }
    @keyframes spin { to { transform:rotate(360deg); } }
    .disabled { opacity:.6; pointer-events:none; }
  `]
})
export class GroupDetailComponent implements OnInit {
  private api = inject(ApiService);
  private route = inject(ActivatedRoute);
  private toast = inject(ToastService);
  items = signal<Contact[]>([]);
  loading = signal(true);
  importing = signal(false);
  page = signal(1);
  total = signal(0);
  pageSize = 50;
  q = ''; valid: boolean | undefined = undefined;
  id = 0;
  sendTo = signal<Contact | null>(null);
  sendBody = '';
  sending = signal(false);
  showAdd = signal(false);
  adding = signal(false);
  addForm = { phone: '', name: '', tags: '' };

  openAdd() { this.addForm = { phone: '', name: '', tags: '' }; this.showAdd.set(true); }
  addContact() {
    if (!this.addForm.phone.trim()) { this.toast.error('Phone number is required'); return; }
    this.adding.set(true);
    this.api.addContact(this.id, { phone: this.addForm.phone, name: this.addForm.name || null, customFields: {}, tags: this.addForm.tags ? this.addForm.tags.split(',').map(x => x.trim()).filter(Boolean) : [] }).subscribe({
      next: () => { this.adding.set(false); this.showAdd.set(false); this.toast.success('Contact added to group'); this.load(); },
      error: e => { this.adding.set(false); this.toast.error(e?.error?.detail || 'Could not add contact'); }
    });
  }

  openSend(c: Contact) { this.sendTo.set(c); this.sendBody = ''; }
  doSend() {
    const c = this.sendTo(); if (!c || !this.sendBody.trim()) return;
    this.sending.set(true);
    this.api.sendTo({ phone: c.phone, body: this.sendBody }).subscribe({
      next: () => { this.sending.set(false); this.sendTo.set(null); this.toast.success('Message sent'); },
      error: ex => { this.sending.set(false); this.toast.error(ex?.error?.detail || 'Send failed'); }
    });
  }

  ngOnInit() { this.id = +this.route.snapshot.params['id']; this.load(); }
  load() {
    this.loading.set(true);
    this.api.groupContacts(this.id, { q: this.q, valid: this.valid, page: this.page(), pageSize: this.pageSize }).subscribe({
      next: r => { this.items.set(r.items); this.total.set(r.totalCount); this.loading.set(false); },
      error: () => { this.loading.set(false); this.toast.error('API unreachable'); }
    });
  }
  go(d: number) { this.page.update(p => Math.max(1, p + d)); this.load(); }
  del(c: Contact) { this.api.deleteContact(c.id).subscribe(() => { this.toast.success('Deleted'); this.load(); }); }
  onFile(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0]; if (!file) return;
    this.importing.set(true);
    this.api.importContacts(this.id, file).subscribe({
      next: r => { this.importing.set(false); input.value = ''; this.toast.success(`Imported ${r.imported}, ${r.duplicates} dupes, ${r.invalid} invalid`); this.load(); },
      error: ex => { this.importing.set(false); input.value = ''; this.toast.error(ex?.error?.detail || 'Import failed'); }
    });
  }
  validateVisible() {
    const phones = this.items().map(c => c.phone);
    this.api.validatePhones(phones).subscribe(r => { const ok = r.filter(x => x.waRegistered).length; this.toast.success(`${ok}/${r.length} on WhatsApp`); });
  }
}
