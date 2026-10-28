import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { ContactGroup, Tag } from '../../core/models';

@Component({
  selector: 'app-contacts',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
  <div class="page-head">
    <h1>Contacts</h1>
    <div class="row-actions">
      <button class="btn btn-ghost" (click)="showTags.set(true); loadTags()">Tags</button>
      <button class="btn btn-primary" (click)="openCreate()">+ New Group</button>
    </div>
  </div>
  <div class="card card-pad" style="margin-bottom:1rem">
    <input class="input" placeholder="Search groups…" [(ngModel)]="q" (keyup.enter)="load()" style="max-width:300px" />
  </div>
  <div class="card">
    <table class="table">
      <thead><tr><th>Group</th><th>Source</th><th>Contacts</th><th>Valid</th><th>Tags</th><th></th></tr></thead>
      <tbody>
        @for (g of items(); track g.id) {
          <tr>
            <td><a [routerLink]="['/contacts', g.id]"><b>{{ g.name }}</b></a></td>
            <td><span class="badge badge-gray">{{ g.source }}</span></td>
            <td>{{ g.totalContacts }}</td>
            <td>{{ g.validContacts }} <span class="muted">/ {{ g.invalidContacts }} invalid</span></td>
            <td>@for (t of g.tags; track t) { <span class="badge badge-blue" style="margin-right:3px">{{ t }}</span> }</td>
            <td class="row-actions"><button class="btn btn-ghost btn-sm" (click)="exportGroup(g)">Export</button><button class="btn btn-danger btn-sm" (click)="remove(g)">Delete</button></td>
          </tr>
        }
        @if (!items().length && !loading()) { <tr><td colspan="6" class="empty">No groups</td></tr> }
      </tbody>
    </table>
  </div>

  @if (showCreate()) {
    <div class="modal-backdrop" (click)="showCreate.set(false)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">New contact group</div>
      <div class="modal-body">
        <div class="field"><label>Name</label><input class="input" [(ngModel)]="name" /></div>
        <div class="field"><label>Tags (comma separated)</label><input class="input" [(ngModel)]="tags" /></div>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="showCreate.set(false)">Cancel</button><button class="btn btn-primary" (click)="create()">Create</button></div>
    </div></div>
  }
  @if (showTags()) {
    <div class="modal-backdrop" (click)="showTags.set(false)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">Tags</div>
      <div class="modal-body">
        <div class="flex gap" style="margin-bottom:1rem">
          <input class="input" placeholder="name" [(ngModel)]="newTag.name" />
          <input class="input" type="color" [(ngModel)]="newTag.color" style="max-width:60px" />
          <button class="btn btn-primary" (click)="addTag()">Add</button>
        </div>
        @for (t of tagList(); track t.id) {
          <div class="between" style="padding:.3rem 0"><span class="badge" [style.background]="t.color" style="color:#fff">{{ t.name }}</span><button class="btn btn-danger btn-sm" (click)="delTag(t)">Delete</button></div>
        }
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="showTags.set(false)">Close</button></div>
    </div></div>
  }
  `
})
export class ContactsComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  items = signal<ContactGroup[]>([]);
  tagList = signal<Tag[]>([]);
  loading = signal(true);
  showCreate = signal(false);
  showTags = signal(false);
  q = ''; name = ''; tags = '';
  newTag = { name: '', color: '#2563eb' };

  ngOnInit() { this.load(); }
  load() {
    this.loading.set(true);
    this.api.groups({ q: this.q, pageSize: 100 }).subscribe({ next: r => { this.items.set(r.items); this.loading.set(false); }, error: () => { this.loading.set(false); this.toast.error('API unreachable'); } });
  }
  openCreate() { this.name = ''; this.tags = ''; this.showCreate.set(true); }
  create() {
    this.api.createGroup({ name: this.name, tags: this.tags ? this.tags.split(',').map(x => x.trim()) : [] }).subscribe({ next: () => { this.toast.success('Group created'); this.showCreate.set(false); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') });
  }
  remove(g: ContactGroup) { if (confirm(`Delete ${g.name}?`)) this.api.deleteGroup(g.id).subscribe({ next: () => { this.toast.success('Deleted'); this.load(); } }); }
  exportGroup(g: ContactGroup) { this.api.exportGroup(g.id, 'csv').subscribe(b => { const u = URL.createObjectURL(b); const a = document.createElement('a'); a.href = u; a.download = `${g.name}.csv`; a.click(); }); }
  loadTags() { this.api.tags().subscribe(r => this.tagList.set(r)); }
  addTag() { this.api.createTag(this.newTag).subscribe(() => { this.newTag = { name: '', color: '#2563eb' }; this.loadTags(); }); }
  delTag(t: Tag) { this.api.deleteTag(t.id).subscribe(() => this.loadTags()); }
}
