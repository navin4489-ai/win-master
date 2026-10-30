using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

/// <summary>
/// Additive omnichannel layer. WhatsApp continues through its existing bridge/channel
/// implementation; this service owns only Facebook Messenger, Instagram and Email.
/// </summary>
public class OmnichannelService
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _enc;
    private readonly IHttpClientFactory _http;
    private readonly AiService _ai;

    public OmnichannelService(AppDbContext db, IEncryptionService enc, IHttpClientFactory http, AiService ai)
    { _db = db; _enc = enc; _http = http; _ai = ai; }

    public async Task<List<ChannelAccountDto>> AccountsAsync(int ownerId) =>
        (await _db.ChannelAccounts.Where(x => x.OwnerId == ownerId).OrderBy(x => x.Channel).ToListAsync())
        .Select(ToDto).ToList();

    public async Task<ChannelAccountDto> CreateAccountAsync(int ownerId, CreateChannelAccountRequest req)
    {
        if (!Enum.TryParse<CommunicationChannel>(req.Channel, true, out var channel) || channel == CommunicationChannel.WhatsApp)
            throw new BusinessException("Select FacebookMessenger, Instagram or Email.");
        var credentials = JsonSerializer.Serialize(new { req.AccessToken, req.Username, req.Password, req.ImapHost, req.ImapPort });
        var account = new ChannelAccount
        {
            OwnerId = ownerId, Channel = channel, DisplayName = req.DisplayName,
            ExternalAccountId = req.ExternalAccountId, EmailAddress = req.EmailAddress,
            SmtpHost = req.SmtpHost, SmtpPort = req.SmtpPort, ImapHost = req.ImapHost,
            ImapPort = req.ImapPort, CredentialsEnc = _enc.Encrypt(credentials), IsActive = req.IsActive
        };
        _db.ChannelAccounts.Add(account);
        await _db.SaveChangesAsync();
        return ToDto(account);
    }

    public async Task DeleteAccountAsync(int ownerId, int id)
    {
        var a = await _db.ChannelAccounts.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId)
            ?? throw new NotFoundException("Channel account not found.");
        _db.ChannelAccounts.Remove(a);
        await _db.SaveChangesAsync();
    }

    public async Task<InboxMessageDto> SendAsync(int ownerId, SendChannelMessageRequest req)
    {
        if (!Enum.TryParse<CommunicationChannel>(req.Channel, true, out var channel) || channel == CommunicationChannel.WhatsApp)
            throw new BusinessException("Use the existing WhatsApp send flow for WhatsApp messages.");
        var account = await _db.ChannelAccounts.FirstOrDefaultAsync(x => x.Id == req.AccountId && x.OwnerId == ownerId && x.IsActive)
            ?? await _db.ChannelAccounts.FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.Channel == channel && x.IsActive);
        if (account is null) throw new BusinessException("No active channel account configured.");

        var result = channel switch
        {
            CommunicationChannel.Email => await SendEmailAsync(account, req, ownerId),
            CommunicationChannel.FacebookMessenger or CommunicationChannel.Instagram => await SendMetaAsync(account, req),
            _ => new SendResult(false, null, "Unsupported channel")
        };
        if (!result.Success) throw new BusinessException(result.Error ?? "Message could not be sent.");

        var thread = await GetOrCreateThreadAsync(ownerId, account, req.Recipient, req.ThreadId);
        var msg = new InboxMessage
        {
            ThreadId = thread.Id, Direction = MessageDirection.Out, Body = req.Body,
            Status = MessageStatus.Sent, WaMessageId = result.MessageId, Channel = channel
        };
        _db.InboxMessages.Add(msg);
        thread.LastMessageAt = DateTime.UtcNow; thread.Preview = req.Body;
        await _db.SaveChangesAsync();
        await UpsertLeadAsync(ownerId, thread, account, req.Recipient);
        return msg.ToDto();
    }

    public async Task HandleWebhookAsync(ChannelWebhookRequest req)
    {
        if (!Enum.TryParse<CommunicationChannel>(req.Channel, true, out var channel) || channel == CommunicationChannel.WhatsApp)
            throw new BusinessException("Invalid omnichannel webhook channel.");
        var account = await _db.ChannelAccounts.FirstOrDefaultAsync(x => x.Channel == channel && x.ExternalAccountId == req.ExternalAccountId && x.IsActive)
            ?? throw new NotFoundException("Channel account not found.");
        var thread = await GetOrCreateThreadAsync(account.OwnerId, account, req.Sender, null);
        _db.InboxMessages.Add(new InboxMessage
        {
            ThreadId = thread.Id, Direction = MessageDirection.In, Body = req.Body,
            Status = null, WaMessageId = req.ExternalMessageId, Channel = channel,
            SentAt = req.Timestamp ?? DateTime.UtcNow
        });
        thread.LastMessageAt = req.Timestamp ?? DateTime.UtcNow; thread.UnreadCount++; thread.Preview = req.Body;
        await _db.SaveChangesAsync();
        await UpsertLeadAsync(account.OwnerId, thread, account, req.Sender);
    }

    public async Task<PagedResult<LeadDto>> LeadsAsync(int ownerId, string? channel, string? status, PageQuery page)
    {
        var q = _db.LeadProfiles.Where(x => x.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(channel) && Enum.TryParse<CommunicationChannel>(channel, true, out var c)) q = q.Where(x => x.Channel == c);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LeadStatus>(status, true, out var s)) q = q.Where(x => x.Status == s);
        var total = await q.CountAsync();
        var list = await q.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return new PagedResult<LeadDto>(list.Select(ToDto).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<LeadDto> UpdateLeadAsync(int ownerId, int id, UpdateLeadRequest req)
    {
        var lead = await _db.LeadProfiles.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId)
            ?? throw new NotFoundException("Lead not found.");
        if (Enum.TryParse<LeadStatus>(req.Status, true, out var status)) lead.Status = status;
        if (req.IsConverted == true && !lead.IsConverted) { lead.IsConverted = true; lead.ConvertedAt = DateTime.UtcNow; lead.Status = LeadStatus.Converted; }
        if (req.IsConverted == false) lead.IsConverted = false;
        if (req.ConversionValue.HasValue) lead.ConversionValue = req.ConversionValue;
        if (req.Tags is not null) lead.Tags = JsonSerializer.Serialize(req.Tags);
        await _db.SaveChangesAsync();
        return ToDto(lead);
    }

    public async Task<LeadAnalytics> AnalyticsAsync(int ownerId)
    {
        var leads = await _db.LeadProfiles.Where(x => x.OwnerId == ownerId).ToListAsync();
        var total = leads.Count;
        var converted = leads.Count(x => x.IsConverted);
        var byChannel = leads.GroupBy(x => x.Channel).Select(g => new ChannelAnalytics(
            g.Key.ToString(), g.Count(), g.Count(x => x.IsConverted),
            g.Count() == 0 ? 0 : Math.Round(g.Count(x => x.IsConverted) * 100m / g.Count(), 2),
            g.Sum(x => x.ConversionValue ?? 0))).ToList();
        return new LeadAnalytics(total, leads.Count(x => x.Status == LeadStatus.New), leads.Count(x => x.Status == LeadStatus.Qualified),
            converted, leads.Count(x => x.Status == LeadStatus.Lost), total == 0 ? 0 : Math.Round(converted * 100m / total, 2),
            leads.Sum(x => x.ConversionValue ?? 0), byChannel, leads.Where(x => x.IsConverted).OrderByDescending(x => x.ConvertedAt).Take(10).Select(ToDto).ToList());
    }

    public async Task<AiConversationAnalysis> AnalyzeConversationAsync(int ownerId, int threadId)
    {
        var thread = await _db.InboxThreads.FirstOrDefaultAsync(x => x.Id == threadId && x.OwnerId == ownerId)
            ?? throw new NotFoundException("Conversation not found.");
        var messages = await _db.InboxMessages.Where(x => x.ThreadId == threadId).OrderBy(x => x.SentAt).Take(100).ToListAsync();
        var transcript = string.Join("\n", messages.Select(m => $"{m.Direction}: {m.Body}"));
        var reply = await _ai.ReplyAsync(ownerId, $"Analyze this customer conversation for sales lead qualification. Return a concise summary, buying intent signals, next action, score from 0-100, and suggested status.\n\n{transcript}");
        var lead = await _db.LeadProfiles.FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.ThreadId == threadId);
        if (lead is not null) { lead.AiSummary = reply.Text; lead.Score = ExtractScore(reply.Text); await _db.SaveChangesAsync(); }
        return new AiConversationAnalysis(reply.Text, Array.Empty<string>(), "Follow up with a personalized offer", lead?.Score ?? 0, lead?.Status.ToString() ?? "New");
    }

    private async Task<LeadProfile> UpsertLeadAsync(int ownerId, InboxThread thread, ChannelAccount account, string identity)
    {
        var lead = await _db.LeadProfiles.FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.ThreadId == thread.Id);
        if (lead is null)
        {
            lead = new LeadProfile { OwnerId = ownerId, ThreadId = thread.Id, DisplayName = thread.ContactName ?? identity,
                Email = account.Channel == CommunicationChannel.Email ? identity : null, Channel = account.Channel };
            _db.LeadProfiles.Add(lead);
        }
        return lead;
    }

    private async Task<InboxThread> GetOrCreateThreadAsync(int ownerId, ChannelAccount account, string identity, string? externalId)
    {
        var thread = await _db.InboxThreads.FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.ChannelAccountId == account.Id && x.ContactPhone == identity);
        if (thread is not null) return thread;
        thread = new InboxThread { OwnerId = ownerId, AccountId = account.Id, Channel = account.Channel, ChannelAccountId = account.Id,
            ContactPhone = identity, ExternalConversationId = externalId, IsBotEnabled = true };
        _db.InboxThreads.Add(thread);
        await _db.SaveChangesAsync();
        return thread;
    }

    private async Task<SendResult> SendMetaAsync(ChannelAccount account, SendChannelMessageRequest req)
    {
        var creds = ReadCredentials(account);
        var token = creds.GetValueOrDefault("AccessToken") ?? throw new BusinessException("Meta access token is missing.");
        var endpoint = account.Channel == CommunicationChannel.Instagram ? "https://graph.facebook.com/v20.0/me/messages" : "https://graph.facebook.com/v20.0/me/messages";
        var client = _http.CreateClient("meta");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { recipient = new { id = req.Recipient }, message = new { text = req.Body } });
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return new SendResult(false, null, $"Meta returned {(int)response.StatusCode}: {json}");
        return new SendResult(true, TryJson(json, "message_id") ?? Guid.NewGuid().ToString("N"), null);
    }

    private async Task<SendResult> SendEmailAsync(ChannelAccount account, SendChannelMessageRequest req, int ownerId)
    {
        var creds = ReadCredentials(account);
        if (string.IsNullOrWhiteSpace(account.SmtpHost) || !account.SmtpPort.HasValue) return new SendResult(false, null, "SMTP host and port are required.");
        try
        {
            using var smtp = new SmtpClient(account.SmtpHost, account.SmtpPort.Value) { EnableSsl = true };
            if (!string.IsNullOrWhiteSpace(creds.GetValueOrDefault("Username")))
                smtp.Credentials = new NetworkCredential(creds.GetValueOrDefault("Username"), creds.GetValueOrDefault("Password"));
            using var mail = new MailMessage(account.EmailAddress ?? creds.GetValueOrDefault("Username") ?? "", req.Recipient)
            { Subject = req.Subject ?? "Message from Win Master", Body = req.Body, IsBodyHtml = false };
            await smtp.SendMailAsync(mail);
            return new SendResult(true, Guid.NewGuid().ToString("N"), null);
        }
        catch (Exception ex) { return new SendResult(false, null, ex.Message); }
    }

    private Dictionary<string, string?> ReadCredentials(ChannelAccount a)
    {
        if (string.IsNullOrWhiteSpace(a.CredentialsEnc)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>( _enc.Decrypt(a.CredentialsEnc)) ?? new(); }
        catch { return new(); }
    }

    private static string? TryJson(string value, string key)
    { try { using var d = JsonDocument.Parse(value); return d.RootElement.TryGetProperty(key, out var p) ? p.GetString() : null; } catch { return null; } }
    private static decimal ExtractScore(string text) => System.Text.RegularExpressions.Regex.Match(text, @"\b([0-9]{1,3})\s*(?:/\s*100|out of 100)\b").Success ? decimal.Parse(System.Text.RegularExpressions.Regex.Match(text, @"\b([0-9]{1,3})\b").Value) : 0;

    private static ChannelAccountDto ToDto(ChannelAccount a) => new(a.Id, a.Channel.ToString(), a.DisplayName, a.ExternalAccountId, a.EmailAddress, a.SmtpHost, a.SmtpPort, a.IsActive, a.LastSyncAt);
    private static LeadDto ToDto(LeadProfile l) => new(l.Id, l.DisplayName, l.Email, l.Phone, l.Channel.ToString(), l.Status.ToString(), l.Score, l.IsConverted, l.ConvertedAt, l.ConversionValue, l.AiSummary, l.CreatedAt);
}
