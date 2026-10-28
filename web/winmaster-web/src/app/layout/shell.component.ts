import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { AuthService } from '../core/auth.service';

interface NavItem { label: string; path: string; icon: string; roles?: string[]; section?: string; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
  <div class="shell">
    <aside class="sidebar">
      <div class="brand">
        <img src="logo.svg" alt="" width="34" height="34" />
        <span>Win Master</span>
      </div>
      <nav>
        <div class="nav-label">Main</div>
        @for (item of visibleNav(); track item.path) {
          @if (item.section) { <div class="nav-label">{{ item.section }}</div> }
          <a [routerLink]="item.path" routerLinkActive="active" class="nav-item">
            <span class="ic">{{ item.icon }}</span>{{ item.label }}
          </a>
        }
      </nav>
      <div class="side-foot muted">v1.0 · WhatsApp Suite</div>
    </aside>
    <div class="main">
      <header class="topbar">
        <div class="muted crumb">WhatsApp Automation &amp; Marketing</div>
        <div class="flex gap" style="align-items:center">
          <div style="text-align:right">
            <div style="font-weight:600;font-size:.86rem">{{ auth.user()?.displayName }}</div>
            <div class="muted" style="font-size:.72rem">{{ auth.user()?.role }}</div>
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
    .shell { display: grid; grid-template-columns: 256px 1fr; min-height: 100vh; }
    .sidebar { background: var(--sidebar); color: #aab7c9; padding: 1.1rem .8rem; position: sticky; top: 0; height: 100vh; overflow:auto; display:flex; flex-direction:column; }
    .brand { display:flex; align-items:center; gap:.6rem; color:#fff; font-weight:800; font-size:1.2rem; letter-spacing:-.02em; padding:.4rem .6rem 1.3rem; }
    .nav-label { font-size:.65rem; text-transform:uppercase; letter-spacing:.08em; color:#5b6b82; font-weight:700; padding:.9rem .7rem .4rem; }
    .nav-item { display:flex; align-items:center; gap:.7rem; padding:.62rem .7rem; border-radius:10px; color:#aab7c9; font-weight:600; font-size:.86rem; margin-bottom:1px; transition:.14s; }
    .nav-item:hover { background: var(--sidebar-hover); color:#fff; text-decoration:none; }
    .nav-item.active { background: linear-gradient(135deg, var(--accent), var(--primary)); color:#fff; box-shadow: 0 6px 16px rgba(18,140,74,.4); }
    .ic { width:20px; text-align:center; font-size:1rem; }
    .side-foot { margin-top:auto; padding:.9rem .7rem .3rem; font-size:.7rem; color:#5b6b82; }
    .topbar { background:#fff; border-bottom:1px solid var(--border); padding:.75rem 1.4rem; display:flex; align-items:center; justify-content:space-between; position:sticky; top:0; z-index:10; }
    .crumb { font-size:.82rem; font-weight:500; }
    .avatar { width:38px; height:38px; border-radius:50%; background:linear-gradient(135deg,var(--accent),var(--primary)); color:#fff; display:flex; align-items:center; justify-content:center; font-weight:700; font-size:.85rem; }
    .content { padding: 1.6rem; max-width: 1440px; }
    @media (max-width: 760px) { .shell { grid-template-columns: 1fr; } .sidebar { display:none; } }
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
    { label: 'Campaigns', path: '/campaigns', icon: '🚀', section: 'Messaging' },
    { label: 'Contacts', path: '/contacts', icon: '👥' },
    { label: 'Inbox', path: '/inbox', icon: '💬' },
    { label: 'Templates', path: '/templates', icon: '📝' },
    { label: 'Accounts', path: '/accounts', icon: '📱', section: 'Infrastructure' },
    { label: 'Proxies', path: '/proxies', icon: '🛰' },
    { label: 'Auto-Reply', path: '/rules', icon: '🤖', section: 'Automation' },
    { label: 'AI Chatbot', path: '/ai', icon: '✨' },
    { label: 'Reports', path: '/reports', icon: '📊', section: 'Insights' },
    { label: 'Licenses', path: '/licenses', icon: '🔑', roles: ['Reseller', 'Admin', 'SuperAdmin'], section: 'Administration' },
    { label: 'Users', path: '/users', icon: '🧑‍💼', roles: ['Admin', 'SuperAdmin'] },
    { label: 'Settings', path: '/settings', icon: '⚙', roles: ['Admin', 'SuperAdmin'] },
  ];

  visibleNav() { return this.nav.filter(n => !n.roles || this.auth.hasRole(...n.roles)); }
  initials() { return (this.auth.user()?.displayName || '?').split(' ').map(x => x[0]).slice(0, 2).join('').toUpperCase(); }
  logout() { this.auth.logout(); this.router.navigate(['/login']); }
}
