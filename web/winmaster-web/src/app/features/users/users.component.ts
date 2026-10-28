import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { AuthService } from '../../core/auth.service';
import { User } from '../../core/models';

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [FormsModule],
  template: `
  <div class="page-head"><h1>Users</h1><button class="btn btn-primary" (click)="open()">+ New User</button></div>
  <div class="card">
    <table class="table">
      <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th></th></tr></thead>
      <tbody>
        @for (u of items(); track u.id) {
          <tr><td><b>{{ u.displayName }}</b></td><td>{{ u.email }}</td><td><span class="badge badge-blue">{{ u.role }}</span></td>
          <td><span class="badge" [class]="u.isActive ? 'badge-green' : 'badge-gray'">{{ u.isActive ? 'Active' : 'Disabled' }}</span></td>
          <td class="row-actions"><button class="btn btn-ghost btn-sm" (click)="toggle(u)">{{ u.isActive ? 'Disable' : 'Enable' }}</button></td></tr>
        }
        @if (!items().length && !loading()) { <tr><td colspan="5" class="empty">No users</td></tr> }
      </tbody>
    </table>
  </div>
  @if (show()) {
    <div class="modal-backdrop" (click)="show.set(false)"><div class="modal" (click)="$event.stopPropagation()">
      <div class="modal-head">New user</div>
      <div class="modal-body">
        <div class="field"><label>Display name</label><input class="input" [(ngModel)]="form.displayName" /></div>
        <div class="field"><label>Email</label><input class="input" [(ngModel)]="form.email" /></div>
        <div class="field"><label>Password</label><input class="input" type="password" [(ngModel)]="form.password" /></div>
        <div class="field"><label>Role</label><select class="input" [(ngModel)]="form.role">@for (r of roles(); track r) { <option>{{ r }}</option> }</select></div>
      </div>
      <div class="modal-foot"><button class="btn btn-ghost" (click)="show.set(false)">Cancel</button><button class="btn btn-primary" (click)="create()">Create</button></div>
    </div></div>
  }
  `
})
export class UsersComponent implements OnInit {
  private api = inject(ApiService); private toast = inject(ToastService); private auth = inject(AuthService);
  items = signal<User[]>([]); loading = signal(true); show = signal(false);
  form: any = {};
  ngOnInit() { this.load(); }
  load() { this.loading.set(true); this.api.users({ pageSize: 100 }).subscribe({ next: r => { this.items.set(r.items); this.loading.set(false); }, error: () => { this.loading.set(false); this.toast.error('API unreachable'); } }); }
  roles() { return this.auth.user()?.role === 'SuperAdmin' ? ['Admin', 'Reseller', 'User'] : ['Reseller', 'User']; }
  open() { this.form = { displayName: '', email: '', password: '', role: 'User', parentUserId: null }; this.show.set(true); }
  create() { this.api.createUser(this.form).subscribe({ next: () => { this.toast.success('User created'); this.show.set(false); this.load(); }, error: e => this.toast.error(e?.error?.detail || 'Failed') }); }
  toggle(u: User) { this.api.updateUser(u.id, { displayName: u.displayName, isActive: !u.isActive }).subscribe(() => { this.toast.success('Updated'); this.load(); }); }
}
