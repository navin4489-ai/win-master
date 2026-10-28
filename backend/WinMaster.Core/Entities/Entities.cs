using System.ComponentModel.DataAnnotations;

namespace WinMaster.Core.Entities;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class Role
{
    public int Id { get; set; }
    public UserRole Name { get; set; }
}

public class User : BaseEntity
{
    [MaxLength(256)] public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    [MaxLength(200)] public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; }
    public int? ParentUserId { get; set; }
    public User? ParentUser { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(40)] public string? ResellerCode { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

public class RefreshToken : BaseEntity
{
    public int UserId { get; set; }
    public User? User { get; set; }
    [MaxLength(200)] public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive => RevokedAt == null && DateTime.UtcNow < ExpiresAt;
}

public class Machine : BaseEntity
{
    public int UserId { get; set; }
    [MaxLength(200)] public string Hwid { get; set; } = "";
    [MaxLength(200)] public string? DeviceId { get; set; }
    [MaxLength(200)] public string? DeviceName { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class License : BaseEntity
{
    [MaxLength(60)] public string Key { get; set; } = "";
    public LicenseType LicenseType { get; set; }
    public int IssuedById { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
    /// <summary>Comma-separated feature flags.</summary>
    [MaxLength(1000)] public string Features { get; set; } = "";
    public DateTime StartsAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public int MaxActivations { get; set; } = 1;
    public LicenseStatus Status { get; set; } = LicenseStatus.Active;

    public ICollection<Activation> Activations { get; set; } = new List<Activation>();
}

public class Activation : BaseEntity
{
    public int LicenseId { get; set; }
    public License? License { get; set; }
    [MaxLength(200)] public string Hwid { get; set; } = "";
    [MaxLength(200)] public string? DeviceName { get; set; }
    public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class Proxy : BaseEntity
{
    public int OwnerId { get; set; }
    public ProxyType Type { get; set; }
    [MaxLength(200)] public string Host { get; set; } = "";
    public int Port { get; set; }
    [MaxLength(200)] public string? Username { get; set; }
    /// <summary>Encrypted at rest.</summary>
    public string? PasswordEnc { get; set; }
    public bool IsHealthy { get; set; } = true;
}

public class WhatsAppAccount : BaseEntity
{
    public int OwnerId { get; set; }
    public EngineType EngineType { get; set; }
    [MaxLength(40)] public string? PhoneNumber { get; set; }
    [MaxLength(200)] public string DisplayName { get; set; } = "";
    public WaAccountStatus Status { get; set; } = WaAccountStatus.Disconnected;
    [MaxLength(100)] public string? SessionId { get; set; }
    public int? ProxyId { get; set; }
    public Proxy? Proxy { get; set; }
    public WarmupState WarmupState { get; set; } = WarmupState.None;
    public int RotationWeight { get; set; } = 1;
    public int DailyLimit { get; set; } = 1000;
    public int SentToday { get; set; }
    public DateTime? LastSeenAt { get; set; }
    /// <summary>Encrypted CloudApi credentials JSON (phoneNumberId, accessToken, wabaId).</summary>
    public string? CredentialsEnc { get; set; }
    public string? QrData { get; set; }
    public DateTime? QrExpiresAt { get; set; }
}

public class ContactGroup : BaseEntity
{
    public int OwnerId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    public ContactSource Source { get; set; } = ContactSource.Manual;
    public int TotalContacts { get; set; }
    public int ValidContacts { get; set; }
    public int InvalidContacts { get; set; }
    [MaxLength(1000)] public string Tags { get; set; } = "";

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
}

public class Contact : BaseEntity
{
    public int GroupId { get; set; }
    public ContactGroup? Group { get; set; }
    [MaxLength(40)] public string Phone { get; set; } = "";
    [MaxLength(200)] public string? Name { get; set; }
    public bool? WaRegistered { get; set; }
    /// <summary>JSON dictionary of custom fields.</summary>
    public string CustomFields { get; set; } = "{}";
    [MaxLength(1000)] public string Tags { get; set; } = "";
    public DateTime? LastMessagedAt { get; set; }
}

public class Tag : BaseEntity
{
    public int OwnerId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(20)] public string Color { get; set; } = "#888888";
}

public class Template : BaseEntity
{
    public int OwnerId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    public string Body { get; set; } = "";
    public MediaType MediaType { get; set; } = MediaType.None;
    [MaxLength(1000)] public string? MediaUrl { get; set; }
    /// <summary>JSON array of buttons.</summary>
    public string? Buttons { get; set; }
    /// <summary>JSON list menu.</summary>
    public string? ListMenu { get; set; }
    [MaxLength(100)] public string Category { get; set; } = "General";
}

public class Campaign : BaseEntity
{
    public int OwnerId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    public int? TemplateId { get; set; }
    public Template? Template { get; set; }
    /// <summary>Inline body override when no template.</summary>
    public string? Body { get; set; }
    public int GroupId { get; set; }
    public ContactGroup? Group { get; set; }
    public EngineType EngineType { get; set; }
    public int? AccountId { get; set; }
    public RotationMode RotationMode { get; set; } = RotationMode.RoundRobin;
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public DateTime? ScheduledAt { get; set; }
    public TimeSpan? WindowStart { get; set; }
    public TimeSpan? WindowEnd { get; set; }
    public bool SpintaxEnabled { get; set; } = true;
    public bool PersonalizeMessage { get; set; } = true;
    public int MinDelaySec { get; set; } = 5;
    public int MaxDelaySec { get; set; } = 15;
    public int BatchSize { get; set; } = 50;
    public int CooldownMinutes { get; set; } = 5;
    public int TotalRecipients { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<CampaignRecipient> Recipients { get; set; } = new List<CampaignRecipient>();
}

public class CampaignRecipient : BaseEntity
{
    public int CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public int ContactId { get; set; }
    [MaxLength(40)] public string Phone { get; set; } = "";
    [MaxLength(200)] public string? Name { get; set; }
    public string? PersonalizationJson { get; set; }
    public RecipientStatus Status { get; set; } = RecipientStatus.Pending;
    public string? VariantText { get; set; }
    [MaxLength(2000)] public string? Error { get; set; }
    [MaxLength(200)] public string? MessageId { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class CampaignCheckpoint : BaseEntity
{
    public int CampaignId { get; set; }
    public int LastRecipientId { get; set; }
    public int SentInBatch { get; set; }
    public int RotationIndex { get; set; }
}

public class AccountAntiBanSettings : BaseEntity
{
    public int AccountId { get; set; }
    public int MinDelaySec { get; set; } = 5;
    public int MaxDelaySec { get; set; } = 15;
    public int BatchSize { get; set; } = 50;
    public int CooldownMinutes { get; set; } = 5;
    public int DailyCap { get; set; } = 1000;
}

public class AutoReplyRule : BaseEntity
{
    public int OwnerId { get; set; }
    public int AccountId { get; set; }
    public MatchType MatchType { get; set; }
    [MaxLength(500)] public string Pattern { get; set; } = "";
    public int Priority { get; set; }
    public string? ResponseText { get; set; }
    public int? ResponseTemplateId { get; set; }
    public bool UseAi { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AiSettings : BaseEntity
{
    public int OwnerId { get; set; }
    public AiProvider? Provider { get; set; }
    public string? ApiKeyEnc { get; set; }
    [MaxLength(100)] public string Model { get; set; } = "gpt-4o-mini";
    public string SystemPrompt { get; set; } = "You are a helpful WhatsApp assistant.";
    public bool IsActive { get; set; }
    public int DailyTokenBudget { get; set; } = 100000;
}

public class InboxThread : BaseEntity
{
    public int OwnerId { get; set; }
    public int AccountId { get; set; }
    [MaxLength(40)] public string ContactPhone { get; set; } = "";
    public int? ContactId { get; set; }
    [MaxLength(200)] public string? ContactName { get; set; }
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
    public int UnreadCount { get; set; }
    public int? AssignedTo { get; set; }
    public bool IsBotEnabled { get; set; } = true;
    [MaxLength(500)] public string? Preview { get; set; }

    public ICollection<InboxMessage> Messages { get; set; } = new List<InboxMessage>();
}

public class InboxMessage : BaseEntity
{
    public int ThreadId { get; set; }
    public InboxThread? Thread { get; set; }
    public MessageDirection Direction { get; set; }
    public string Body { get; set; } = "";
    [MaxLength(1000)] public string? MediaUrl { get; set; }
    public MediaType? MediaType { get; set; }
    public MessageStatus? Status { get; set; }
    [MaxLength(200)] public string? WaMessageId { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class WebhookEvent : BaseEntity
{
    [MaxLength(50)] public string Provider { get; set; } = "";
    [MaxLength(200)] public string ExternalId { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime? ProcessedAt { get; set; }
}

public class PushToken : BaseEntity
{
    public int UserId { get; set; }
    [MaxLength(20)] public string Platform { get; set; } = "";
    [MaxLength(500)] public string Token { get; set; } = "";
    [MaxLength(200)] public string? DeviceId { get; set; }
}

public class AuditLog : BaseEntity
{
    public int? UserId { get; set; }
    [MaxLength(100)] public string Action { get; set; } = "";
    [MaxLength(100)] public string? Entity { get; set; }
    [MaxLength(100)] public string? EntityId { get; set; }
    [MaxLength(60)] public string? Ip { get; set; }
    [MaxLength(1000)] public string? MaskedDetail { get; set; }
}

public class AppSetting : BaseEntity
{
    [MaxLength(100)] public string Key { get; set; } = "";
    public string ValueJson { get; set; } = "";
}
