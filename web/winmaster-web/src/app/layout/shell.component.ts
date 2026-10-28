import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { AuthService } from '../core/auth.service';

interface NavItem { label: string; path: string; icon: string; roles?: string[]; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
  <div class="shell">
    <aside class="sidebar">
      <div class="brand">
        <span class="logo">✺</span>
        <span>Win Master</span>
      </div>
      <nav>
        @for (item of visibleNav(); track item.path) {
          <a [routerLink]="item.path" routerLinkActive="active" class="nav-item">
            <span class="ic">{{ item.icon }}</span>{{ item.label }}
          </a>
        }
      </nav>
    </aside>
    <div class="main">
      <header class="topbar">
        <div class="muted">WhatsApp Automation &amp; Marketing</div>
        <div class="flex gap" style="align-items:center">
          <div style="text-align:right">
            <div style="font-weight:600">{{ auth.user()?.displayName }}</div>
            <div class="muted" style="font-size:.75rem">{{ auth.user()?.role }}</div>
          </div>
          <div class="avatar">{{ initials() }}</div>
          <button class="btn btn-ghost btn-sm" (click)="logout()">Logout</button>
        </div>
      </header>
      <section class="content"><router-outlet></router-outlet></section>
    </div>
  </div>
  `,
  styles: [`
    .shell { display: grid; grid-template-columns: 250px 1fr; min-height: 100vh; }
    .sidebar { background: var(--bg); color: #cbd5e1; padding: 1rem .75rem; position: sticky; top: 0; height: 100vh; overflow:auto; }
    .brand { display:flex; align-items:center; gap:.6rem; color:#fff; font-weight:750; font-size:1.15rem; padding:.5rem .6rem 1.1rem; }
    .logo { color: var(--accent); font-size: 1.4rem; }
    .nav-item { display:flex; align-items:center; gap:.65rem; padding:.6rem .7rem; border-radius:9px; color:#cbd5e1; font-weight:600; font-size:.87rem; margin-bottom:2px; }
    .nav-item:hover { background: rgba(255,255,255,.06); color:#fff; }
    .nav-item.active { background: var(--primary); color:#fff; }
    .ic { width:20px; text-align:center; }
    .topbar { background:#fff; border-bottom:1px solid var(--border); padding:.7rem 1.3rem; display:flex; align-items:center; justify-content:space-between; position:sticky; top:0; z-index:10; }
    .avatar { width:38px; height:38px; border-radius:50%; background:var(--primary); color:#fff; display:flex; align-items:center; justify-content:center; font-weight:700; }
    .content { padding: 1.4rem; max-width: 1400px; }
  `]
})
export class ShellComponent {
  auth = inject(AuthService);
  private router = inject(Router);

  constructor() {
    // Hydrate the current user (role-based menu) if we have a token but no user yet.
    if (this.auth.accessToken && !this.auth.user()) {
      this.auth.loadMe().subscribe({ error: () => { this.auth.logout(); this.router.navigate(['/login']); } });
    }
  }
  nav: NavItem[] = [
    { label: 'Dashboard', path: '/dashboard', icon: '▣' },
    { label: 'Campaigns', path: '/campaigns', icon: '🚀' },
    { label: 'Contacts', path: '/contacts', icon: '👥' },
    { label: 'Inbox', path: '/inbox', icon: '💬' },
    { label: 'Accounts', path: '/accounts', icon: '📱' },
    { label: 'Proxies', path: '/proxies', icon: '🛰' },
    { label: 'Templates', path: '/templates', icon: '📝' },
    { label: 'Auto-Reply', path: '/rules', icon: '🤖' },
    { label: 'AI Chatbot', path: '/ai', icon: '✨' },
    { label: 'Reports', path: '/reports', icon: '📊' },
    { label: 'Licenses', path: '/licenses', icon: '🔑', roles: ['Reseller', 'Admin', 'SuperAdmin'] },
    { label: 'Users', path: '/users', icon: '🧑‍💼', roles: ['Admin', 'SuperAdmin'] },
    { label: 'Settings', path: '/settings', icon: '⚙', roles: ['Admin', 'SuperAdmin'] },
  ];

  visibleNav() { return this.nav.filter(n => !n.roles || this.auth.hasRole(...n.roles)); }
  initials() { return (this.auth.user()?.displayName || '?').split(' ').map(x => x[0]).slice(0, 2).join('').toUpperCase(); }
  logout() { this.auth.logout(); this.router.navigate(['/login']); }
}
