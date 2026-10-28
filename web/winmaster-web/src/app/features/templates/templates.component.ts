import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { Template } from '../../core/models';

@Component({
  selector: 'app-templates',
  standalone: true,
  imports: [FormsModule],
  template: `
  <div class="page-head"><h1>Templates</h1><button class="btn btn-primary" (click)="open()">+ New Template</button></div>
  <div class="stat-grid">
    @for (t of items(); track t.id) {
      <div class="card card-pad">
        <div class="between"><b>{{ t.name }}</b><span class="badge badge-gray">{{ t.category }}</span></div>
        <p class="muted" style="font-size:.85rem;min-height:40px">{{ t.body }}</p>
        <div class="muted" style="font-size:.78rem">Media: {{ t.mediaType }}</div>
        <div class="row-actions" style="margin-top:.6rem"><button class="btn btn-ghost btn-sm" (click)="edit(t)">Edit</button><button class="btn btn-danger btn-sm" (click)="del(t)">Delete</button></div>
      </div>
    }
    @if (!items().length && !loading()) { <div class="empty">No templates</div> }
  </div>
  @if (show()) {
    <div class="modal-backdrop" (click)="show.set(false)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">{{ form.id ? 'Edit' : 'New' }} template</div>
      <div class="modal-body">
        <div class="field"><label>Name</label><input class="input" [(ngModel)]="form.name" /></div>
        <div class="field"><label>Category</label><input class="input" [(ngModel)]="form.category" /></div>
        <div class="field"><label>Body (supports {{ '{{Name}}' }} and {{ '{a|b}' }} spintax)</label><textarea class="input" rows="4" [(ngModel)]="form.body"></textarea></div>
        <div class="flex gap"><div class="field" style="flex:1"><label>Media type</label><select class="input" [(ngModel)]="form.mediaType"><option>None</option><option>Image</option><option>Document</option><option>Video</option><option>Audio</option></select></div><div class="field" style="flex:2"><label>Media URL</label><input class="input" [(ngModel)]="form.mediaUrl" /></div></div>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="show.set(false)">Cancel</button><button class="btn btn-primary" (click)="save()">Save</button></div>
    </div></div>
  }
  `
})
export class TemplatesComponent implements OnInit {
  private api = inject(ApiService); private toast = inject(ToastService);
  items = signal<Template[]>([]); loading = signal(true); show = signal(false);
  form: any = {};
  ngOnInit() { this.load(); }
  load() { this.loading.set(true); this.api.templates().subscribe({ next: r => { this.items.set(r); this.loading.set(false); }, error: () => { this.loading.set(false); this.toast.error('API unreachable'); } }); }
  open() { this.form = { name: '', category: 'General', body: '', mediaType: 'None', mediaUrl: '' }; this.show.set(true); }
  edit(t: Template) { this.form = { ...t }; this.show.set(true); }
  save() {
    const op = this.form.id ? this.api.updateTemplate(this.form.id, this.form) : this.api.createTemplate(this.form);
    op.subscribe({ next: () => { this.toast.success('Saved'); this.show.set(false); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') });
  }
  del(t: Template) { this.api.deleteTemplate(t.id).subscribe(() => { this.toast.success('Deleted'); this.load(); }); }
}
