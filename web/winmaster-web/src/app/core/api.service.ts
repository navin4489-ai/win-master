import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import * as M from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private http = inject(HttpClient);
  private base = environment.apiUrl + '/api';

  private params(obj: Record<string, any>): HttpParams {
    let p = new HttpParams();
    Object.entries(obj).forEach(([k, v]) => { if (v !== undefined && v !== null && v !== '') p = p.set(k, String(v)); });
    return p;
  }

  // Users
  users(q: any) { return this.http.get<M.Paged<M.User>>(`${this.base}/users`, { params: this.params(q) }); }
  createUser(b: any) { return this.http.post<M.User>(`${this.base}/users`, b); }
  updateUser(id: number, b: any) { return this.http.put<M.User>(`${this.base}/users/${id}`, b); }
  deleteUser(id: number) { return this.http.delete(`${this.base}/users/${id}`); }
  userTree() { return this.http.get<M.UserTreeNode[]>(`${this.base}/users/tree`); }

  // Licenses
  licenses(q: any) { return this.http.get<M.Paged<M.License>>(`${this.base}/licenses`, { params: this.params(q) }); }
  createLicense(b: any) { return this.http.post<M.License>(`${this.base}/licenses`, b); }
  revokeLicense(id: number) { return this.http.post<M.License>(`${this.base}/licenses/${id}/revoke`, {}); }
  activations(id: number) { return this.http.get<M.Activation[]>(`${this.base}/licenses/${id}/activations`); }
  revokeDevice(id: number) { return this.http.delete(`${this.base}/licenses/activations/${id}`); }

  // Accounts
  accounts() { return this.http.get<M.WaAccount[]>(`${this.base}/whatsapp-accounts`); }
  accountsStatus() { return this.http.get<M.AccountsStatus>(`${this.base}/whatsapp-accounts/status`); }
  createAccount(b: any) { return this.http.post<M.WaAccount>(`${this.base}/whatsapp-accounts`, b); }
  updateAccount(id: number, b: any) { return this.http.put<M.WaAccount>(`${this.base}/whatsapp-accounts/${id}`, b); }
  deleteAccount(id: number) { return this.http.delete(`${this.base}/whatsapp-accounts/${id}`); }
  connectAccount(id: number) { return this.http.post<M.WaAccount>(`${this.base}/whatsapp-accounts/${id}/connect`, {}); }
  accountQr(id: number) { return this.http.get<M.Qr>(`${this.base}/whatsapp-accounts/${id}/qr`); }
  disconnectAccount(id: number) { return this.http.post(`${this.base}/whatsapp-accounts/${id}/disconnect`, {}); }

  // Proxies
  proxies() { return this.http.get<M.Proxy[]>(`${this.base}/proxies`); }
  createProxy(b: any) { return this.http.post<M.Proxy>(`${this.base}/proxies`, b); }
  updateProxy(id: number, b: any) { return this.http.put<M.Proxy>(`${this.base}/proxies/${id}`, b); }
  deleteProxy(id: number) { return this.http.delete(`${this.base}/proxies/${id}`); }
  testProxy(id: number) { return this.http.post<any>(`${this.base}/proxies/${id}/test`, {}); }

  // Contact groups + contacts
  groups(q: any) { return this.http.get<M.Paged<M.ContactGroup>>(`${this.base}/contact-groups`, { params: this.params(q) }); }
  createGroup(b: any) { return this.http.post<M.ContactGroup>(`${this.base}/contact-groups`, b); }
  updateGroup(id: number, b: any) { return this.http.put<M.ContactGroup>(`${this.base}/contact-groups/${id}`, b); }
  deleteGroup(id: number) { return this.http.delete(`${this.base}/contact-groups/${id}`); }
  groupContacts(id: number, q: any) { return this.http.get<M.Paged<M.Contact>>(`${this.base}/contact-groups/${id}/contacts`, { params: this.params(q) }); }
  addContact(id: number, b: any) { return this.http.post<M.Contact>(`${this.base}/contact-groups/${id}/contacts`, b); }
  updateContact(id: number, b: any) { return this.http.put<M.Contact>(`${this.base}/contacts/${id}`, b); }
  deleteContact(id: number) { return this.http.delete(`${this.base}/contacts/${id}`); }
  importContacts(id: number, file: File, columnMapJson?: string) {
    const form = new FormData(); form.append('file', file);
    if (columnMapJson) form.append('columnMapJson', columnMapJson);
    return this.http.post<M.ImportResult>(`${this.base}/contact-groups/${id}/import`, form);
  }
  validatePhones(phones: string[]) { return this.http.post<M.ValidateResult[]>(`${this.base}/contact-groups/validate`, { phones }); }
  exportGroup(id: number, format: string) { return this.http.get(`${this.base}/contact-groups/${id}/export`, { params: this.params({ format }), responseType: 'blob' }); }
  scrapeGroup(id: number, b: any) { return this.http.post<any>(`${this.base}/contact-groups/${id}/scrape`, b); }

  // Tags
  tags() { return this.http.get<M.Tag[]>(`${this.base}/tags`); }
  createTag(b: any) { return this.http.post<M.Tag>(`${this.base}/tags`, b); }
  updateTag(id: number, b: any) { return this.http.put<M.Tag>(`${this.base}/tags/${id}`, b); }
  deleteTag(id: number) { return this.http.delete(`${this.base}/tags/${id}`); }

  // Templates
  templates() { return this.http.get<M.Template[]>(`${this.base}/templates`); }
  createTemplate(b: any) { return this.http.post<M.Template>(`${this.base}/templates`, b); }
  updateTemplate(id: number, b: any) { return this.http.put<M.Template>(`${this.base}/templates/${id}`, b); }
  deleteTemplate(id: number) { return this.http.delete(`${this.base}/templates/${id}`); }

  // Campaigns
  campaigns(q: any) { return this.http.get<M.Paged<M.Campaign>>(`${this.base}/campaigns`, { params: this.params(q) }); }
  campaign(id: number) { return this.http.get<M.Campaign>(`${this.base}/campaigns/${id}`); }
  createCampaign(b: any) { return this.http.post<M.Campaign>(`${this.base}/campaigns`, b); }
  updateCampaign(id: number, b: any) { return this.http.put<M.Campaign>(`${this.base}/campaigns/${id}`, b); }
  deleteCampaign(id: number) { return this.http.delete(`${this.base}/campaigns/${id}`); }
  scheduleCampaign(id: number, b: any) { return this.http.post<M.Campaign>(`${this.base}/campaigns/${id}/schedule`, b); }
  campaignAction(id: number, action: string) { return this.http.post<M.Campaign>(`${this.base}/campaigns/${id}/${action}`, {}); }
  campaignProgress(id: number) { return this.http.get<M.CampaignProgress>(`${this.base}/campaigns/${id}/progress`); }
  campaignRecipients(id: number, q: any) { return this.http.get<M.Paged<M.CampaignRecipient>>(`${this.base}/campaigns/${id}/recipients`, { params: this.params(q) }); }
  campaignReport(id: number, format: string) { return this.http.get(`${this.base}/campaigns/${id}/report`, { params: this.params({ format }), responseType: 'blob' }); }

  // Anti-ban
  antiBan(accountId: number) { return this.http.get<M.AntiBan>(`${this.base}/anti-ban/${accountId}`); }
  updateAntiBan(accountId: number, b: any) { return this.http.put<M.AntiBan>(`${this.base}/anti-ban/${accountId}`, b); }

  // Rules
  rules(accountId?: number) { return this.http.get<M.AutoReplyRule[]>(`${this.base}/auto-reply-rules`, { params: this.params({ accountId }) }); }
  createRule(b: any) { return this.http.post<M.AutoReplyRule>(`${this.base}/auto-reply-rules`, b); }
  updateRule(id: number, b: any) { return this.http.put<M.AutoReplyRule>(`${this.base}/auto-reply-rules/${id}`, b); }
  deleteRule(id: number) { return this.http.delete(`${this.base}/auto-reply-rules/${id}`); }
  toggleRule(id: number) { return this.http.put<M.AutoReplyRule>(`${this.base}/auto-reply-rules/${id}/toggle`, {}); }

  // AI
  aiSettings() { return this.http.get<M.AiSettings>(`${this.base}/ai-settings`); }
  updateAi(b: any) { return this.http.put<M.AiSettings>(`${this.base}/ai-settings`, b); }
  testAi(message: string) { return this.http.post<any>(`${this.base}/ai-settings/test`, { message }); }

  // Inbox
  threads(q: any) { return this.http.get<M.Paged<M.InboxThread>>(`${this.base}/inbox/threads`, { params: this.params(q) }); }
  messages(threadId: number, q: any) { return this.http.get<M.Paged<M.InboxMessage>>(`${this.base}/inbox/threads/${threadId}/messages`, { params: this.params(q) }); }
  sendMessage(threadId: number, b: any) { return this.http.post<M.InboxMessage>(`${this.base}/inbox/threads/${threadId}/messages`, b); }
  toggleBot(threadId: number, isBotEnabled: boolean) { return this.http.put(`${this.base}/inbox/threads/${threadId}/bot`, { isBotEnabled }); }
  markRead(threadId: number) { return this.http.post(`${this.base}/inbox/threads/${threadId}/read`, {}); }

  // Reports / dashboard
  dashboard() { return this.http.get<M.DashboardStats>(`${this.base}/dashboard/stats`); }
  delivery(q: any) { return this.http.get<M.DeliveryReport>(`${this.base}/reports/delivery`, { params: this.params(q) }); }
  kpis(q: any) { return this.http.get<M.KpiReport>(`${this.base}/reports/kpis`, { params: this.params(q) }); }

  // Settings / audit
  settings() { return this.http.get<any>(`${this.base}/settings`); }
  updateSettings(b: any) { return this.http.put(`${this.base}/settings`, b); }
  auditLogs(q: any) { return this.http.get<M.Paged<M.AuditLog>>(`${this.base}/audit-logs`, { params: this.params(q) }); }
}
