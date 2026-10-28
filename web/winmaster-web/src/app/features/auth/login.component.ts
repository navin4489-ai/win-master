import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule],
  template: `
  <div class="wrap">
    <div class="card card-pad box">
      <div class="brand"><span class="logo">✺</span> Win Master</div>
      <p class="muted" style="margin-top:-.4rem">Sign in to your marketing console</p>
      <div class="field">
        <label>Email</label>
        <input class="input" [(ngModel)]="email" placeholder="admin@winmaster.io" (keyup.enter)="submit()" />
      </div>
      <div class="field">
        <label>Password</label>
        <input class="input" type="password" [(ngModel)]="password" placeholder="••••••••" (keyup.enter)="submit()" />
      </div>
      <button class="btn btn-primary" style="width:100%" (click)="submit()" [disabled]="loading()">
        {{ loading() ? 'Signing in…' : 'Sign in' }}
      </button>
      <div class="demo muted">
        Demo accounts: {{ demo }}
      </div>
    </div>
  </div>
  `,
  styles: [`
    .wrap { min-height:100vh; display:flex; align-items:center; justify-content:center; background: linear-gradient(135deg,#0f172a,#15803d); padding:1rem; }
    .box { width:100%; max-width:380px; }
    .brand { display:flex; align-items:center; gap:.5rem; font-size:1.5rem; font-weight:750; }
    .logo { color: var(--accent); }
    .demo { margin-top:1rem; font-size:.75rem; text-align:center; }
  `]
})
export class LoginComponent {
  private auth = inject(AuthService);
  private router = inject(Router);
  private toast = inject(ToastService);
  email = 'admin@winmaster.io';
  password = 'Win@12345';
  loading = signal(false);
  demo = 'admin@winmaster.io · reseller@winmaster.io · user@winmaster.io / Win@12345';

  submit() {
    if (!this.email || !this.password) return;
    this.loading.set(true);
    this.auth.login(this.email, this.password).subscribe({
      next: () => { this.loading.set(false); this.router.navigate(['/dashboard']); },
      error: (e) => { this.loading.set(false); this.toast.error(e?.error?.detail || 'Invalid credentials or API unreachable'); }
    });
  }
}
