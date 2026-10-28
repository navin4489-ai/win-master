import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/guards';
import { ShellComponent } from './layout/shell.component';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent) },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'campaigns', loadComponent: () => import('./features/campaigns/campaigns.component').then(m => m.CampaignsComponent) },
      { path: 'campaigns/:id', loadComponent: () => import('./features/campaigns/campaign-detail.component').then(m => m.CampaignDetailComponent) },
      { path: 'contacts', loadComponent: () => import('./features/contacts/contacts.component').then(m => m.ContactsComponent) },
      { path: 'contacts/:id', loadComponent: () => import('./features/contacts/group-detail.component').then(m => m.GroupDetailComponent) },
      { path: 'inbox', loadComponent: () => import('./features/inbox/inbox.component').then(m => m.InboxComponent) },
      { path: 'accounts', loadComponent: () => import('./features/accounts/accounts.component').then(m => m.AccountsComponent) },
      { path: 'proxies', loadComponent: () => import('./features/proxies/proxies.component').then(m => m.ProxiesComponent) },
      { path: 'templates', loadComponent: () => import('./features/templates/templates.component').then(m => m.TemplatesComponent) },
      { path: 'rules', loadComponent: () => import('./features/rules/rules.component').then(m => m.RulesComponent) },
      { path: 'ai', loadComponent: () => import('./features/ai/ai.component').then(m => m.AiComponent) },
      { path: 'reports', loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent) },
      { path: 'licenses', canActivate: [roleGuard('Reseller', 'Admin', 'SuperAdmin')], loadComponent: () => import('./features/licenses/licenses.component').then(m => m.LicensesComponent) },
      { path: 'users', canActivate: [roleGuard('Admin', 'SuperAdmin')], loadComponent: () => import('./features/users/users.component').then(m => m.UsersComponent) },
      { path: 'settings', canActivate: [roleGuard('Admin', 'SuperAdmin')], loadComponent: () => import('./features/settings/settings.component').then(m => m.SettingsComponent) },
    ]
  },
  { path: '**', redirectTo: '' }
];
