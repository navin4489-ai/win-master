using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

/// <summary>Processes callbacks from the Node/Baileys bridge and Meta webhooks (idempotent).</summary>
public class BridgeService
{
    private readonly AppDbContext _db;
    private readonly AiService _ai;
    private readonly IWhatsAppChannelFactory _channels;
    public BridgeService(AppDbContext db, AiService ai, IWhatsAppChannelFactory channels)
    { _db = db; _ai = ai; _channels = channels; }

    public async Task HandleAsync(BridgeEvent ev)
    {
        switch (ev.Type)
        {
            case "qr":
                await WithAccount(ev.AccountId, a => { a.Status = WaAccountStatus.QrReady; a.QrData = Str(ev.Payload, "qrData"); a.QrPngBase64 = Str(ev.Payload, "qrPngBase64"); a.QrExpiresAt = DateTime.UtcNow.AddSeconds(60); });
                break;
            case "connected":
                await WithAccount(ev.AccountId, a => { a.Status = WaAccountStatus.Connected; a.PhoneNumber = Str(ev.Payload, "phoneNumber") ?? a.PhoneNumber; a.LastSeenAt = DateTime.UtcNow; a.QrData = null; a.QrPngBase64 = null; });
                break;
            case "disconnected":
                await WithAccount(ev.AccountId, a => a.Status = WaAccountStatus.Disconnected);
                break;
            case "message":
                await IngestInboundAsync(ev);
                break;
            case "message_status":
                await UpdateStatusAsync(ev);
                break;
            case "scrape_progress":
                // Could persist a scrape job table; no-op placeholder.
                break;
        }
    }

    private async Task IngestInboundAsync(BridgeEvent ev)
    {
        var from = PhoneUtils.Normalize(Str(ev.Payload, "from") ?? "");
        var waId = Str(ev.Payload, "waMessageId") ?? Guid.NewGuid().ToString("N");
        if (await _db.WebhookEvents.AnyAsync(w => w.Provider == "bridge" && w.ExternalId == waId)) return;
        _db.WebhookEvents.Add(new WebhookEvent { Provider = "bridge", ExternalId = waId, Payload = ev.Payload.GetRawText(), ProcessedAt = DateTime.UtcNow });

        var acc = await _db.WhatsAppAccounts.FindAsync(ev.AccountId);
        if (acc is null) { await _db.SaveChangesAsync(); return; }
        var thread = await _db.InboxThreads.FirstOrDefaultAsync(t => t.AccountId == ev.AccountId && t.ContactPhone == from);
        if (thread is null)
        {
            thread = new InboxThread { OwnerId = acc.OwnerId, AccountId = ev.AccountId, ContactPhone = from };
            _db.InboxThreads.Add(thread);
            await _db.SaveChangesAsync();
        }
        var body = Str(ev.Payload, "body") ?? "";
        _db.InboxMessages.Add(new InboxMessage { ThreadId = thread.Id, Direction = MessageDirection.In, Body = body, WaMessageId = waId });
        thread.LastMessageAt = DateTime.UtcNow; thread.UnreadCount++; thread.Preview = body;
        await _db.SaveChangesAsync();

        await TryAutoReplyAsync(acc, thread, body);
    }

    /// <summary>Rule-based + AI auto-responder. Runs only when the thread's bot is enabled.</summary>
    private async Task TryAutoReplyAsync(WhatsAppAccount acc, InboxThread thread, string inbound)
    {
        if (!thread.IsBotEnabled || string.IsNullOrWhiteSpace(inbound)) return;

        var rules = await _db.AutoReplyRules
            .Where(r => r.AccountId == acc.Id && r.IsActive)
            .OrderBy(r => r.Priority).ToListAsync();
        var match = RuleService.Match(rules, inbound);

        string? replyText;
        if (match is not null && !match.UseAi)
        {
            replyText = match.ResponseText;
            if (string.IsNullOrEmpty(replyText) && match.ResponseTemplateId is int tid)
                replyText = (await _db.Templates.FindAsync(tid))?.Body;
        }
        else
        {
            // AI path: an AI-flagged rule matched, OR no rule matched but AI is active.
            var ai = await _db.AiSettings.FirstOrDefaultAsync(x => x.OwnerId == acc.OwnerId);
            if (match is null && ai?.IsActive != true) return; // nothing to say
            var reply = await _ai.ReplyAsync(acc.OwnerId, inbound);
            replyText = reply.Text;
        }
        if (string.IsNullOrWhiteSpace(replyText)) return;

        var channel = _channels.For(acc.EngineType);
        var result = await channel.SendAsync(acc.Id,
            new OutboundMessage(thread.ContactPhone, replyText, null, MediaType.None, null, null));
        _db.InboxMessages.Add(new InboxMessage
        {
            ThreadId = thread.Id, Direction = MessageDirection.Out, Body = replyText,
            Status = result.Success ? MessageStatus.Sent : MessageStatus.Failed, WaMessageId = result.MessageId
        });
        thread.LastMessageAt = DateTime.UtcNow; thread.Preview = replyText;
        await _db.SaveChangesAsync();
    }

    private async Task UpdateStatusAsync(BridgeEvent ev)
    {
        var waId = Str(ev.Payload, "waMessageId");
        var status = Str(ev.Payload, "status");
        if (waId is null || status is null) return;
        var recipient = await _db.CampaignRecipients.FirstOrDefaultAsync(r => r.MessageId == waId);
        if (recipient is not null)
        {
            recipient.Status = status.ToLower() switch
            {
                "delivered" => RecipientStatus.Delivered,
                "read" => RecipientStatus.Read,
                "failed" => RecipientStatus.Failed,
                _ => recipient.Status
            };
            if (status == "delivered") recipient.DeliveredAt = DateTime.UtcNow;
            if (status == "read") recipient.ReadAt = DateTime.UtcNow;
        }
        var msg = await _db.InboxMessages.FirstOrDefaultAsync(m => m.WaMessageId == waId);
        if (msg is not null)
            msg.Status = status.ToLower() switch { "delivered" => MessageStatus.Delivered, "read" => MessageStatus.Read, "failed" => MessageStatus.Failed, _ => msg.Status };
        await _db.SaveChangesAsync();
    }

    private async Task WithAccount(int accountId, Action<WhatsAppAccount> mutate)
    {
        var acc = await _db.WhatsAppAccounts.FindAsync(accountId);
        if (acc is null) return;
        mutate(acc);
        await _db.SaveChangesAsync();
    }

    private static string? Str(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) ? v.ToString() : null;
}

public class PushTokenService
{
    private readonly AppDbContext _db;
    public PushTokenService(AppDbContext db) => _db = db;

    public async Task RegisterAsync(int userId, PushTokenRequest req)
    {
        var existing = await _db.PushTokens.FirstOrDefaultAsync(t => t.Token == req.Token);
        if (existing is null)
            _db.PushTokens.Add(new PushToken { UserId = userId, Platform = req.Platform, Token = req.Token, DeviceId = req.DeviceId });
        else { existing.UserId = userId; existing.Platform = req.Platform; existing.DeviceId = req.DeviceId; }
        await _db.SaveChangesAsync();
    }

    public async Task RemoveAsync(string token)
    {
        var t = await _db.PushTokens.FirstOrDefaultAsync(x => x.Token == token);
        if (t is not null) { _db.PushTokens.Remove(t); await _db.SaveChangesAsync(); }
    }
}
