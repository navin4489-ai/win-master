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
        <div class="flex gap">
          <div class="field" style="flex:1"><label>Media type</label><select class="input" [(ngModel)]="form.mediaType"><option>None</option><option>Image</option><option>Document</option><option>Video</option><option>Audio</option></select></div>
          <div class="field" style="flex:2"><label>Attachment</label>
            @if (form.mediaType && form.mediaType !== 'None') {
              <div class="flex gap-sm" style="align-items:center">
                <label class="btn btn-ghost btn-sm" style="cursor:pointer;white-space:nowrap" [class.disabled]="uploading()">
                  @if (uploading()) { <span class="spinner"></span> Uploading… } @else { Browse… }
                  <input type="file" hidden [accept]="accept()" [disabled]="uploading()" (change)="onFile($event)" />
                </label>
                @if (form.mediaUrl) {
                  <span class="muted" style="font-size:.8rem;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1" [title]="form.mediaUrl">{{ fileLabel() }}</span>
                  <button class="btn btn-ghost btn-sm" (click)="clearFile()" title="Remove">✕</button>
                } @else {
                  <span class="muted" style="font-size:.8rem">No file chosen</span>
                }
              </div>
            } @else {
              <input class="input" disabled placeholder="Select a media type first" />
            }
          </div>
        </div>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="show.set(false)">Cancel</button><button class="btn btn-primary" (click)="save()">Save</button></div>
    </div></div>
  }
  `,
  styles: [`
    .spinner { display:inline-block; width:13px; height:13px; margin-right:5px; vertical-align:-2px;
      border:2px solid rgba(255,255,255,.35); border-top-color:currentColor; border-radius:50%;
      animation:spin .6s linear infinite; }
    @keyframes spin { to { transform:rotate(360deg); } }
    .disabled { opacity:.6; pointer-events:none; }
  `]
})
export class TemplatesComponent implements OnInit {
  private api = inject(ApiService); private toast = inject(ToastService);
  items = signal<Template[]>([]); loading = signal(true); show = signal(false);
  uploading = signal(false);
  form: any = {};
  ngOnInit() { this.load(); }
  load() { this.loading.set(true); this.api.templates().subscribe({ next: r => { this.items.set(r); this.loading.set(false); }, error: () => { this.loading.set(false); this.toast.error('API unreachable'); } }); }
  open() { this.form = { name: '', category: 'General', body: '', mediaType: 'None', mediaUrl: '' }; this.show.set(true); }
  edit(t: Template) { this.form = { ...t }; this.show.set(true); }

  accept() {
    switch (this.form.mediaType) {
      case 'Image': return 'image/*';
      case 'Video': return 'video/*';
      case 'Audio': return 'audio/*';
      case 'Document': return '.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv';
      default: return '*/*';
    }
  }
  fileLabel() { const u = this.form.mediaUrl || ''; return u.split('/').pop() || u; }
  onFile(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0]; if (!file) return;
    this.uploading.set(true);
    this.api.uploadMedia(file).subscribe({
      next: r => { this.form.mediaUrl = r.url; this.uploading.set(false); input.value = ''; this.toast.success('File uploaded'); },
      error: ex => { this.uploading.set(false); input.value = ''; this.toast.error(ex?.error?.detail || 'Upload failed'); }
    });
  }
  clearFile() { this.form.mediaUrl = ''; }

  save() {
    const op = this.form.id ? this.api.updateTemplate(this.form.id, this.form) : this.api.createTemplate(this.form);
    op.subscribe({ next: () => { this.toast.success('Saved'); this.show.set(false); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') });
  }
  del(t: Template) { this.api.deleteTemplate(t.id).subscribe(() => { this.toast.success('Deleted'); this.load(); }); }
}
