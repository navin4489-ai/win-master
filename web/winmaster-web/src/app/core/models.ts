export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export type Role = 'User' | 'Reseller' | 'Admin' | 'SuperAdmin';

export interface User {
  id: number; email: string; displayName: string; role: Role;
  parentUserId?: number | null; isActive: boolean; resellerCode?: string | null; license?: License | null;
}
export interface AuthResponse { accessToken: string; refreshToken: string; expiresIn: number; user: User; }

export interface License {
  id: number; key: string; licenseType: string; issuedById: number; userId?: number | null; userEmail?: string | null;
  features: string[]; startsAt: string; expiresAt?: string | null; maxActivations: number; status: string;
  createdAt: string; activationCount: number;
}
export interface Activation { id: number; licenseId: number; hwid: string; deviceName?: string; activatedAt: string; lastVerifiedAt?: string; revokedAt?: string; }

export interface WaAccount {
  id: number; engineType: string; phoneNumber?: string; displayName: string; status: string;
  proxyId?: number | null; proxyLabel?: string | null; warmupState: string; rotationWeight: number;
  dailyLimit: number; sentToday: number; lastSeenAt?: string; sessionId?: string;
}
export interface AccountsStatus { connected: number; connecting: number; total: number; }
export interface Qr { qrData?: string; qrPngBase64?: string; expiresAt?: string; }
export interface Proxy { id: number; type: string; host: string; port: number; username?: string; isHealthy: boolean; }

export interface ContactGroup {
  id: number; name: string; source: string; totalContacts: number; validContacts: number;
  invalidContacts: number; tags: string[]; createdAt: string; updatedAt?: string;
}
export interface Contact { id: number; groupId: number; phone: string; name?: string; waRegistered?: boolean | null; customFields: Record<string, string>; tags: string[]; lastMessagedAt?: string; }
export interface Tag { id: number; name: string; color: string; }
export interface ImportResult { imported: number; invalid: number; duplicates: number; errors: string[]; }
export interface ValidateResult { phone: string; valid: boolean; waRegistered?: boolean | null; reason?: string; }

export interface TemplateButton { type: string; id: string; title: string; payload?: string; }
export interface Template {
  id: number; name: string; body: string; mediaType: string; mediaUrl?: string;
  buttons?: TemplateButton[]; listMenu?: any; category: string; createdAt: string; updatedAt?: string;
}

export interface Pacing { minDelaySec: number; maxDelaySec: number; batchSize: number; cooldownMinutes: number; }
export interface Campaign {
  id: number; name: string; templateId?: number | null; templateName?: string; groupId: number; groupName?: string;
  engineType: string; accountId?: number | null; rotationMode: string; status: string; scheduledAt?: string;
  windowStart?: string; windowEnd?: string; spintaxEnabled: boolean; personalizeMessage: boolean; pacing: Pacing;
  totalRecipients: number; processedCount: number; failedCount: number; progressPercent: number;
  startedAt?: string; completedAt?: string; createdAt: string;
}
export interface CampaignProgress {
  campaignId: number; status: string; total: number; processed: number; sent: number; delivered: number;
  read: number; failed: number; currentRecipientIndex?: number; estimatedSecondsRemaining?: number; lastCheckpointAt?: string;
}
export interface CampaignRecipient { id: number; phoneMasked: string; name?: string; status: string; variantText?: string; error?: string; sentAt?: string; deliveredAt?: string; readAt?: string; }

export interface AntiBan { minDelaySec: number; maxDelaySec: number; batchSize: number; cooldownMinutes: number; dailyCap: number; }
export interface AutoReplyRule { id: number; accountId: number; matchType: string; pattern: string; priority: number; responseText?: string; responseTemplateId?: number; useAi: boolean; isActive: boolean; }
export interface AiSettings { provider?: string | null; model: string; systemPrompt: string; isActive: boolean; dailyTokenBudget: number; maskedApiKey?: string; }

export interface InboxThread { id: number; accountId: number; accountLabel: string; contactPhone: string; contactPhoneMasked: string; contactName?: string; lastMessageAt: string; unreadCount: number; isBotEnabled: boolean; preview?: string; }
export interface InboxMessage { id: number; threadId: number; direction: string; body: string; mediaUrl?: string; mediaType?: string; status?: string; waMessageId?: string; sentAt: string; createdAt: string; }

export interface DeliveryReport { sent: number; delivered: number; read: number; failed: number; deliveryRate: number; readRate: number; series: { date: string; sent: number; delivered: number; read: number; failed: number; }[]; }
export interface KpiReport { deliveryRate: number; crashFreeSessions?: number; suspensionRate: number; totalCampaigns: number; totalMessages: number; activeAccounts: number; activeLicenses: number; }
export interface DashboardStats {
  campaigns: { total: number; running: number; scheduled: number; };
  messages: { sentToday: number; deliveredToday: number; failedToday: number; };
  accounts: { connected: number; total: number; };
  contacts: { total: number; groups: number; };
  licenses: { active: number; expiringSoon: number; };
  inbox: { unread: number; threads: number; };
}
export interface AuditLog { id: number; userId?: number; action: string; entity?: string; entityId?: string; ip?: string; maskedDetail?: string; createdAt: string; }
export interface UserTreeNode { id: number; email: string; displayName: string; role: string; children: UserTreeNode[]; }
