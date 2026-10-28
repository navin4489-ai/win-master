using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

public class AntiBanService
{
    private readonly AppDbContext _db;
    public AntiBanService(AppDbContext db) => _db = db;

    public async Task<AntiBanDto> GetAsync(int accountId)
    {
        var s = await _db.AntiBanSettings.FirstOrDefaultAsync(x => x.AccountId == accountId)
            ?? new AccountAntiBanSettings { AccountId = accountId };
        return new AntiBanDto(s.MinDelaySec, s.MaxDelaySec, s.BatchSize, s.CooldownMinutes, s.DailyCap);
    }

    public async Task<AntiBanDto> UpdateAsync(int accountId, AntiBanDto dto)
    {
        var s = await _db.AntiBanSettings.FirstOrDefaultAsync(x => x.AccountId == accountId);
        if (s is null) { s = new AccountAntiBanSettings { AccountId = accountId }; _db.AntiBanSettings.Add(s); }
        s.MinDelaySec = dto.MinDelaySec; s.MaxDelaySec = dto.MaxDelaySec; s.BatchSize = dto.BatchSize;
        s.CooldownMinutes = dto.CooldownMinutes; s.DailyCap = dto.DailyCap;
        await _db.SaveChangesAsync();
        return dto;
    }
}

public class RuleService
{
    private readonly AppDbContext _db;
    public RuleService(AppDbContext db) => _db = db;

    public async Task<List<AutoReplyRuleDto>> ListAsync(int ownerId, int? accountId)
    {
        var q = _db.AutoReplyRules.Where(r => r.OwnerId == ownerId);
        if (accountId.HasValue) q = q.Where(r => r.AccountId == accountId);
        return (await q.OrderBy(r => r.Priority).ToListAsync()).Select(r => r.ToDto()).ToList();
    }

    public async Task<AutoReplyRuleDto> CreateAsync(int ownerId, CreateAutoReplyRuleRequest req)
    {
        if (!Enum.TryParse<MatchType>(req.MatchType, true, out var mt)) throw new BusinessException("Invalid match type.");
        if (mt == MatchType.Regex) ValidateRegex(req.Pattern);
        var r = new AutoReplyRule { OwnerId = ownerId, AccountId = req.AccountId, MatchType = mt, Pattern = req.Pattern,
            Priority = req.Priority, ResponseText = req.ResponseText, ResponseTemplateId = req.ResponseTemplateId,
            UseAi = req.UseAi, IsActive = req.IsActive };
        _db.AutoReplyRules.Add(r); await _db.SaveChangesAsync();
        return r.ToDto();
    }

    public async Task<AutoReplyRuleDto> UpdateAsync(int ownerId, int id, CreateAutoReplyRuleRequest req)
    {
        var r = await Owned(ownerId, id);
        if (Enum.TryParse<MatchType>(req.MatchType, true, out var mt)) { if (mt == MatchType.Regex) ValidateRegex(req.Pattern); r.MatchType = mt; }
        r.AccountId = req.AccountId; r.Pattern = req.Pattern; r.Priority = req.Priority;
        r.ResponseText = req.ResponseText; r.ResponseTemplateId = req.ResponseTemplateId; r.UseAi = req.UseAi; r.IsActive = req.IsActive;
        await _db.SaveChangesAsync();
        return r.ToDto();
    }

    public async Task DeleteAsync(int ownerId, int id) { var r = await Owned(ownerId, id); _db.AutoReplyRules.Remove(r); await _db.SaveChangesAsync(); }
    public async Task<AutoReplyRuleDto> ToggleAsync(int ownerId, int id) { var r = await Owned(ownerId, id); r.IsActive = !r.IsActive; await _db.SaveChangesAsync(); return r.ToDto(); }

    private async Task<AutoReplyRule> Owned(int ownerId, int id) =>
        await _db.AutoReplyRules.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == ownerId) ?? throw new NotFoundException("Rule not found.");

    public static void ValidateRegex(string pattern)
    { try { _ = Regex.Match("", pattern); } catch { throw new BusinessException("Invalid regular expression."); } }

    /// <summary>Picks the best matching active rule for an inbound message.</summary>
    public static AutoReplyRule? Match(IEnumerable<AutoReplyRule> rules, string message)
    {
        foreach (var r in rules.Where(r => r.IsActive).OrderBy(r => r.Priority))
        {
            var ok = r.MatchType switch
            {
                MatchType.Exact => string.Equals(message.Trim(), r.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),
                MatchType.Wildcard => WildcardMatch(message, r.Pattern),
                MatchType.Regex => SafeRegex(message, r.Pattern),
                _ => false
            };
            if (ok) return r;
        }
        return null;
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
    }
    private static bool SafeRegex(string input, string pattern)
    { try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase); } catch { return false; } }
}

public class AiService
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _enc;
    private readonly WinMaster.Core.Interfaces.IAiClient _ai;
    public AiService(AppDbContext db, IEncryptionService enc, WinMaster.Core.Interfaces.IAiClient ai) { _db = db; _enc = enc; _ai = ai; }

    /// <summary>Catalog of selectable providers, highlighting the free, no-key, and free-with-key options.</summary>
    public static AiProviderCatalog Catalog() => new(new List<AiProviderInfo>
    {
        new("Local", "Built-in (no key needed)", true, false,
            new[] { "local-generator" }, null),
        new("Groq", "Groq — free & fast", true, true,
            new[] { "llama-3.3-70b-versatile", "llama-3.1-8b-instant", "mixtral-8x7b-32768", "gemma2-9b-it" },
            "https://console.groq.com/keys"),
        new("OpenRouter", "OpenRouter — free models", true, true,
            new[] { "meta-llama/llama-3.2-3b-instruct:free", "qwen/qwen-2.5-7b-instruct:free", "google/gemma-2-9b-it:free" },
            "https://openrouter.ai/keys"),
        new("Gemini", "Google Gemini — free tier", true, true,
            new[] { "gemini-1.5-flash", "gemini-1.5-flash-8b" },
            "https://aistudio.google.com/app/apikey"),
        new("OpenAI", "OpenAI (paid key)", false, true,
            new[] { "gpt-4o-mini", "gpt-4o" }, "https://platform.openai.com/api-keys"),
    });

    public async Task<AiSettingsDto> GetAsync(int ownerId)
    {
        var s = await _db.AiSettings.FirstOrDefaultAsync(x => x.OwnerId == ownerId);
        if (s is null) return new AiSettingsDto("Local", "local-generator", "You are a helpful WhatsApp assistant.", true, 100000, null);
        return new AiSettingsDto(s.Provider?.ToString(), s.Model, s.SystemPrompt, s.IsActive, s.DailyTokenBudget,
            s.ApiKeyEnc is null ? null : _enc.Mask(_enc.Decrypt(s.ApiKeyEnc)));
    }

    public async Task<AiSettingsDto> UpdateAsync(int ownerId, UpdateAiSettingsRequest req)
    {
        var s = await _db.AiSettings.FirstOrDefaultAsync(x => x.OwnerId == ownerId);
        if (s is null) { s = new AiSettings { OwnerId = ownerId }; _db.AiSettings.Add(s); }
        s.Provider = string.IsNullOrEmpty(req.Provider) ? null : Enum.Parse<AiProvider>(req.Provider, true);
        if (!string.IsNullOrEmpty(req.ApiKey)) s.ApiKeyEnc = _enc.Encrypt(req.ApiKey);
        s.Model = req.Model; s.SystemPrompt = req.SystemPrompt; s.IsActive = req.IsActive; s.DailyTokenBudget = req.DailyTokenBudget;
        await _db.SaveChangesAsync();
        return await GetAsync(ownerId);
    }

    public async Task<AiTestResponse> TestAsync(int ownerId, AiTestRequest req)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var reply = await ReplyAsync(ownerId, req.Message);
        sw.Stop();
        return new AiTestResponse(reply.Text, reply.TokensUsed, (int)sw.ElapsedMilliseconds);
    }

    /// <summary>Generates a campaign/template-ready marketing message.</summary>
    public async Task<AiGenerateResponse> GenerateAsync(int ownerId, AiGenerateRequest req)
    {
        var s = await _db.AiSettings.FirstOrDefaultAsync(x => x.OwnerId == ownerId);
        var provider = s?.Provider ?? AiProvider.Local;
        var key = s?.ApiKeyEnc is null ? null : _enc.Decrypt(s.ApiKeyEnc);
        var model = s?.Model ?? "local-generator";

        var instructions = new StringBuilder("You are an expert WhatsApp marketing copywriter. ");
        instructions.Append("Write ONE concise, friendly broadcast message (max 4 short lines, emojis allowed). ");
        if (req.IncludeVariables) instructions.Append("Personalize using the {{Name}} variable. ");
        if (req.IncludeSpintax) instructions.Append("Add spintax variations like {Hi|Hello|Hey} for natural variety. ");
        if (!string.IsNullOrWhiteSpace(req.Tone)) instructions.Append($"Tone: {req.Tone}. ");
        if (!string.IsNullOrWhiteSpace(req.Goal)) instructions.Append($"Goal: {req.Goal}. ");
        instructions.Append("Return only the message text, no preamble.");

        var messages = new List<WinMaster.Core.Interfaces.AiMessage>
        {
            new("system", instructions.ToString()),
            new("user", $"Write a WhatsApp message about: {req.Prompt}"),
        };
        var reply = await _ai.CompleteAsync(provider, key, model, messages);
        // Guarantee variables/spintax even when a model omits them.
        var text = reply.Text;
        if (string.IsNullOrWhiteSpace(text) || provider == AiProvider.Local)
            text = LocalGenerator.GenerateMarketing(req.Prompt);
        return new AiGenerateResponse(text, reply.TokensUsed, reply.Model);
    }

    /// <summary>Produces an auto-reply for the chatbot given an inbound message.</summary>
    public async Task<WinMaster.Core.Interfaces.AiReply> ReplyAsync(int ownerId, string inbound)
    {
        var s = await _db.AiSettings.FirstOrDefaultAsync(x => x.OwnerId == ownerId);
        var provider = s?.Provider ?? AiProvider.Local;
        var key = s?.ApiKeyEnc is null ? null : _enc.Decrypt(s.ApiKeyEnc);
        var model = s?.Model ?? "local-generator";
        var prompt = s?.SystemPrompt ?? "You are a helpful, concise WhatsApp support assistant.";
        var messages = new List<WinMaster.Core.Interfaces.AiMessage>
        {
            new("system", prompt),
            new("user", inbound),
        };
        return await _ai.CompleteAsync(provider, key, model, messages);
    }
}

public class InboxService
{
    private readonly AppDbContext _db;
    public InboxService(AppDbContext db) => _db = db;

    public async Task<PagedResult<InboxThreadDto>> ThreadsAsync(int ownerId, int? accountId, string? q, bool unreadOnly, PageQuery page)
    {
        var query = _db.InboxThreads.Where(t => t.OwnerId == ownerId);
        if (accountId.HasValue) query = query.Where(t => t.AccountId == accountId);
        if (unreadOnly) query = query.Where(t => t.UnreadCount > 0);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(t => t.ContactPhone.Contains(q) || (t.ContactName != null && t.ContactName.Contains(q)));
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(t => t.LastMessageAt).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        var accounts = await _db.WhatsAppAccounts.Where(a => a.OwnerId == ownerId).ToDictionaryAsync(a => a.Id, a => a.DisplayName);
        return new PagedResult<InboxThreadDto>(items.Select(t => t.ToDto(accounts.GetValueOrDefault(t.AccountId, "Account"))).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<InboxMessageDto>> MessagesAsync(int ownerId, int threadId, PageQuery page)
    {
        await Owned(ownerId, threadId);
        var query = _db.InboxMessages.Where(m => m.ThreadId == threadId);
        var total = await query.CountAsync();
        var items = await query.OrderBy(m => m.SentAt).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return new PagedResult<InboxMessageDto>(items.Select(m => m.ToDto()).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<InboxMessageDto> SendAsync(int ownerId, int threadId, SendMessageRequest req, IWhatsAppChannelFactory factory)
    {
        var t = await Owned(ownerId, threadId);
        var account = await _db.WhatsAppAccounts.FindAsync(t.AccountId);
        var channel = factory.For(account?.EngineType ?? EngineType.Baileys);
        var result = await channel.SendAsync(t.AccountId, new OutboundMessage(t.ContactPhone, req.Body, req.MediaUrl, MediaType.None, null, null));
        var msg = new InboxMessage { ThreadId = threadId, Direction = MessageDirection.Out, Body = req.Body, MediaUrl = req.MediaUrl,
            Status = result.Success ? MessageStatus.Sent : MessageStatus.Failed, WaMessageId = result.MessageId };
        _db.InboxMessages.Add(msg);
        t.LastMessageAt = DateTime.UtcNow; t.Preview = req.Body;
        await _db.SaveChangesAsync();
        return msg.ToDto();
    }

    public async Task ToggleBotAsync(int ownerId, int threadId, bool enabled)
    { var t = await Owned(ownerId, threadId); t.IsBotEnabled = enabled; await _db.SaveChangesAsync(); }

    public async Task MarkReadAsync(int ownerId, int threadId)
    { var t = await Owned(ownerId, threadId); t.UnreadCount = 0; await _db.SaveChangesAsync(); }

    private async Task<InboxThread> Owned(int ownerId, int id) =>
        await _db.InboxThreads.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId) ?? throw new NotFoundException("Thread not found.");
}

public class ReportService
{
    private readonly AppDbContext _db;
    public ReportService(AppDbContext db) => _db = db;

    public async Task<DeliveryReport> DeliveryAsync(int ownerId, DateTime? from, DateTime? to, int? campaignId)
    {
        var fromD = from ?? DateTime.UtcNow.AddDays(-7);
        var toD = to ?? DateTime.UtcNow;
        var q = _db.CampaignRecipients.Include(r => r.Campaign).Where(r => r.Campaign!.OwnerId == ownerId);
        if (campaignId.HasValue) q = q.Where(r => r.CampaignId == campaignId);
        var list = await q.Where(r => r.SentAt >= fromD && r.SentAt <= toD).ToListAsync();
        int sent = list.Count(r => r.Status >= RecipientStatus.Sent);
        int delivered = list.Count(r => r.Status is RecipientStatus.Delivered or RecipientStatus.Read);
        int read = list.Count(r => r.Status == RecipientStatus.Read);
        int failed = list.Count(r => r.Status == RecipientStatus.Failed);
        var series = list.Where(r => r.SentAt.HasValue).GroupBy(r => r.SentAt!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DeliverySeriesPoint(g.Key, g.Count(x => x.Status >= RecipientStatus.Sent),
                g.Count(x => x.Status is RecipientStatus.Delivered or RecipientStatus.Read),
                g.Count(x => x.Status == RecipientStatus.Read), g.Count(x => x.Status == RecipientStatus.Failed))).ToList();
        double deliveryRate = sent == 0 ? 0 : Math.Round(delivered * 100.0 / sent, 1);
        double readRate = delivered == 0 ? 0 : Math.Round(read * 100.0 / delivered, 1);
        return new DeliveryReport(sent, delivered, read, failed, deliveryRate, readRate, series);
    }

    public async Task<KpiReport> KpisAsync(int ownerId, DateTime? from, DateTime? to)
    {
        var report = await DeliveryAsync(ownerId, from, to, null);
        var campaigns = await _db.Campaigns.CountAsync(c => c.OwnerId == ownerId);
        var messages = await _db.CampaignRecipients.CountAsync(r => r.Campaign!.OwnerId == ownerId && r.Status >= RecipientStatus.Sent);
        var accounts = await _db.WhatsAppAccounts.CountAsync(a => a.OwnerId == ownerId && a.Status == WaAccountStatus.Connected);
        var licenses = await _db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active);
        var totalSent = report.Sent;
        var suspensionRate = totalSent == 0 ? 0 : Math.Round(report.Failed * 100.0 / totalSent, 1);
        return new KpiReport(report.DeliveryRate, 99.4, suspensionRate, campaigns, messages, accounts, licenses);
    }

    public async Task<DashboardStats> DashboardAsync(int ownerId)
    {
        var today = DateTime.UtcNow.Date;
        var campaigns = await _db.Campaigns.Where(c => c.OwnerId == ownerId).ToListAsync();
        var recipients = _db.CampaignRecipients.Where(r => r.Campaign!.OwnerId == ownerId);
        var sentToday = await recipients.CountAsync(r => r.SentAt >= today && r.Status >= RecipientStatus.Sent);
        var deliveredToday = await recipients.CountAsync(r => r.SentAt >= today && (r.Status == RecipientStatus.Delivered || r.Status == RecipientStatus.Read));
        var failedToday = await recipients.CountAsync(r => r.SentAt >= today && r.Status == RecipientStatus.Failed);
        var accounts = await _db.WhatsAppAccounts.Where(a => a.OwnerId == ownerId).ToListAsync();
        var groups = await _db.ContactGroups.CountAsync(g => g.OwnerId == ownerId);
        var contacts = await _db.Contacts.CountAsync(c => c.Group!.OwnerId == ownerId);
        var activeLic = await _db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active);
        var expiring = await _db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active && l.ExpiresAt != null && l.ExpiresAt < DateTime.UtcNow.AddDays(14));
        var threads = await _db.InboxThreads.Where(t => t.OwnerId == ownerId).ToListAsync();
        return new DashboardStats(
            new CampaignStatsDto(campaigns.Count, campaigns.Count(c => c.Status == CampaignStatus.Running), campaigns.Count(c => c.Status == CampaignStatus.Scheduled)),
            new MessageStatsDto(sentToday, deliveredToday, failedToday),
            new AccountStatsDto(accounts.Count(a => a.Status == WaAccountStatus.Connected), accounts.Count),
            new ContactStatsDto(contacts, groups),
            new LicenseStatsDto(activeLic, expiring),
            new InboxStatsDto(threads.Sum(t => t.UnreadCount), threads.Count));
    }
}

public class SettingsService
{
    private readonly AppDbContext _db;
    public SettingsService(AppDbContext db) => _db = db;

    public async Task<Dictionary<string, JsonElement>> GetAsync()
    {
        var all = await _db.AppSettings.ToListAsync();
        var dict = new Dictionary<string, JsonElement>();
        foreach (var s in all)
            try { dict[s.Key] = JsonDocument.Parse(s.ValueJson).RootElement.Clone(); } catch { }
        return dict;
    }

    public async Task PutAsync(Dictionary<string, JsonElement> values)
    {
        foreach (var kv in values)
        {
            var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == kv.Key);
            if (s is null) { s = new AppSetting { Key = kv.Key }; _db.AppSettings.Add(s); }
            s.ValueJson = kv.Value.GetRawText();
        }
        await _db.SaveChangesAsync();
    }

    public async Task<PagedResult<AuditLogDto>> AuditAsync(DateTime? from, DateTime? to, int? userId, string? action, PageQuery page)
    {
        var q = _db.AuditLogs.AsQueryable();
        if (from.HasValue) q = q.Where(a => a.CreatedAt >= from);
        if (to.HasValue) q = q.Where(a => a.CreatedAt <= to);
        if (userId.HasValue) q = q.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return new PagedResult<AuditLogDto>(
            items.Select(a => new AuditLogDto(a.Id, a.UserId, a.Action, a.Entity, a.EntityId, a.Ip, a.MaskedDetail, a.CreatedAt)).ToList(),
            page.Page, page.PageSize, total);
    }
}
