using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;

namespace WinMaster.Infrastructure.Services;

public static class Mapping
{
    public static UserDto ToDto(this User u, LicenseDto? license = null) =>
        new(u.Id, u.Email, u.DisplayName, u.Role.ToString(), u.ParentUserId, u.IsActive, u.ResellerCode, license);

    public static LicenseDto ToDto(this License l) =>
        new(l.Id, l.Key, l.LicenseType.ToString(), l.IssuedById, l.UserId, l.User?.Email,
            string.IsNullOrEmpty(l.Features) ? Array.Empty<string>() : l.Features.Split(',', StringSplitOptions.RemoveEmptyEntries),
            l.StartsAt, l.ExpiresAt, l.MaxActivations, EffectiveStatus(l).ToString(), l.CreatedAt,
            l.Activations?.Count(a => a.RevokedAt == null) ?? 0);

    public static LicenseStatus EffectiveStatus(License l) =>
        l.Status == LicenseStatus.Revoked ? LicenseStatus.Revoked
        : (l.ExpiresAt.HasValue && l.ExpiresAt < DateTime.UtcNow ? LicenseStatus.Expired : LicenseStatus.Active);

    public static ActivationDto ToDto(this Activation a) =>
        new(a.Id, a.LicenseId, a.Hwid, a.DeviceName, a.ActivatedAt, a.LastVerifiedAt, a.RevokedAt);

    public static ProxyDto ToDto(this Proxy p) => new(p.Id, p.Type.ToString(), p.Host, p.Port, p.Username, p.IsHealthy);

    public static WaAccountDto ToDto(this WhatsAppAccount a) =>
        new(a.Id, a.EngineType.ToString(), a.PhoneNumber, a.DisplayName, a.Status.ToString(), a.ProxyId,
            a.Proxy?.Host, a.WarmupState.ToString(), a.RotationWeight, a.DailyLimit, a.SentToday, a.LastSeenAt, a.SessionId);

    public static ContactGroupDto ToDto(this ContactGroup g) =>
        new(g.Id, g.Name, g.Source.ToString(), g.TotalContacts, g.ValidContacts, g.InvalidContacts,
            Split(g.Tags), g.CreatedAt, g.UpdatedAt);

    public static ContactDto ToDto(this Contact c) =>
        new(c.Id, c.GroupId, c.Phone, c.Name, c.WaRegistered, ParseFields(c.CustomFields), Split(c.Tags), c.LastMessagedAt);

    public static TagDto ToDto(this Tag t) => new(t.Id, t.Name, t.Color);

    public static AutoReplyRuleDto ToDto(this AutoReplyRule r) =>
        new(r.Id, r.AccountId, r.MatchType.ToString(), r.Pattern, r.Priority, r.ResponseText, r.ResponseTemplateId, r.UseAi, r.IsActive);

    public static InboxThreadDto ToDto(this InboxThread t, string accountLabel) =>
        new(t.Id, t.AccountId, accountLabel, t.ContactPhone, PhoneUtils.Mask(t.ContactPhone), t.ContactName,
            t.LastMessageAt, t.UnreadCount, t.IsBotEnabled, t.Preview);

    public static InboxMessageDto ToDto(this InboxMessage m) =>
        new(m.Id, m.ThreadId, m.Direction.ToString(), m.Body, m.MediaUrl, m.MediaType?.ToString(),
            m.Status?.ToString(), m.WaMessageId, m.SentAt, m.CreatedAt);

    public static CampaignRecipientDto ToDto(this CampaignRecipient r) =>
        new(r.Id, PhoneUtils.Mask(r.Phone), r.Name, r.Status.ToString(), r.VariantText, r.Error, r.SentAt, r.DeliveredAt, r.ReadAt);

    public static string[] Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? Array.Empty<string>() : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Dictionary<string, string> ParseFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch { return new(); }
    }
}

/// <summary>Role hierarchy helper (SuperAdmin ⊃ Admin ⊃ Reseller ⊃ User).</summary>
public static class Hierarchy
{
    public static bool CanManage(UserRole actor, UserRole target) => (int)actor > (int)target;
    public static bool AtLeast(UserRole actor, UserRole min) => (int)actor >= (int)min;
}

public class NotFoundException : Exception { public NotFoundException(string m) : base(m) { } }
public class ForbiddenException : Exception { public ForbiddenException(string m) : base(m) { } }
public class BusinessException : Exception { public BusinessException(string m) : base(m) { } }
public class ConflictException : Exception { public ConflictException(string m) : base(m) { } }
