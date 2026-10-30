import { Component, OnInit, inject, signal } from '@angular/core';
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
    <div class="hero">
      <div class="hero-inner">
        <div class="brand"><img src="logo.svg" alt="Win Master" width="40" height="40" /><span>Win Master</span></div>
        <h1>WhatsApp marketing,<br/>automation &amp; CRM — in one place.</h1>
        <p>Bulk broadcasts, smart auto-replies, mini-CRM, anti-ban pacing and multi-tier licensing. Built for agencies, resellers and teams.</p>
        <ul class="feat">
          <li><span>🚀</span> Bulk campaigns with spintax &amp; personalization</li>
          <li><span>🤖</span> Rule &amp; AI powered auto-responder</li>
          <li><span>🔑</span> Reseller &amp; license management</li>
          <li><span>📊</span> Live delivery analytics</li>
        </ul>
      </div>
    </div>

    <div class="form-side">
      <div class="box">
        <div class="brand mobile-brand"><img src="logo.svg" alt="" width="36" height="36" /><span>Win Master</span></div>
        <h2>Welcome back</h2>
        <p class="muted sub">Sign in to your marketing console</p>
        <div class="field">
          <label>Email</label>
          <input class="input" [(ngModel)]="email" autocomplete="username" placeholder="you@company.com" (keyup.enter)="submit()" />
        </div>
        <div class="field">
          <label>Password</label>
          <input class="input" type="password" [(ngModel)]="password" autocomplete="current-password" placeholder="••••••••" (keyup.enter)="submit()" />
        </div>
        <label class="remember"><input type="checkbox" [(ngModel)]="rememberMe" /> <span>Remember me</span></label>
        <button class="btn btn-primary" style="width:100%;height:44px;font-size:.92rem" (click)="submit()" [disabled]="loading()">
          {{ loading() ? 'Signing in…' : 'Sign in' }}
        </button>
      </div>
    </div>
  </div>
  `,
  styles: [`
    .wrap { min-height:100vh; display:grid; grid-template-columns: 1.1fr 1fr; }
    .hero { background: radial-gradient(1200px 600px at -10% -10%, #1b9c5a 0%, transparent 55%), linear-gradient(145deg,#0b1220,#0f7a40); color:#fff; display:flex; align-items:center; padding:3rem; }
    .hero-inner { max-width:460px; }
    .hero .brand { color:#fff; margin-bottom:2.2rem; }
    .hero h1 { font-size:2.1rem; line-height:1.2; margin-bottom:1rem; }
    .hero p { color:rgba(255,255,255,.82); font-size:1rem; line-height:1.6; }
    .feat { list-style:none; padding:0; margin:2rem 0 0; display:grid; gap:.8rem; }
    .feat li { display:flex; align-items:center; gap:.7rem; color:rgba(255,255,255,.92); font-weight:500; font-size:.92rem; }
    .feat li span { width:34px; height:34px; display:grid; place-items:center; background:rgba(255,255,255,.12); border-radius:10px; }
    .brand { display:flex; align-items:center; gap:.6rem; font-size:1.45rem; font-weight:800; letter-spacing:-.02em; }
    .form-side { display:flex; align-items:center; justify-content:center; padding:2rem; background:var(--panel-2); }
    .box { width:100%; max-width:380px; }
    .mobile-brand { display:none; color:var(--text); margin-bottom:1.5rem; }
    .box h2 { font-size:1.7rem; margin-bottom:.25rem; }
    .sub { margin-top:0; margin-bottom:1.6rem; }
    .remember { display:flex; align-items:center; gap:.4rem; margin:-.15rem 0 1rem; color:var(--muted); font-size:.8rem; cursor:pointer; }
    .remember input { accent-color:var(--primary); }
    @media (max-width: 900px) { .wrap { grid-template-columns: 1fr; } .hero { display:none; } .mobile-brand { display:flex; } }
  `]
})
export class LoginComponent implements OnInit {
  private auth = inject(AuthService);
  private router = inject(Router);
  private toast = inject(ToastService);
  email = '';
  password = '';
  rememberMe = false;
  loading = signal(false);

  ngOnInit() {
    const saved = localStorage.getItem('winmaster_saved_credentials');
    if (saved) {
      try {
        const credentials = JSON.parse(saved) as { email?: string; password?: string };
        this.email = credentials.email ?? '';
        this.password = credentials.password ?? '';
        this.rememberMe = true;
      } catch {
        localStorage.removeItem('winmaster_saved_credentials');
      }
    }
  }

  submit() {
    if (!this.email || !this.password) return;
    if (this.rememberMe) {
      localStorage.setItem('winmaster_saved_credentials', JSON.stringify({ email: this.email, password: this.password }));
    } else {
      localStorage.removeItem('winmaster_saved_credentials');
    }
    this.loading.set(true);
    this.auth.login(this.email, this.password).subscribe({
      next: () => { this.loading.set(false); this.router.navigate(['/dashboard']); },
      error: (e) => { this.loading.set(false); this.toast.error(e?.error?.detail || 'Invalid credentials or API unreachable'); }
    });
  }
}
