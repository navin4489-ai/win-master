using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

public class CampaignService
{
    private readonly AppDbContext _db;
    private readonly IWaBridgeClient _bridge;
    public CampaignService(AppDbContext db, IWaBridgeClient bridge) { _db = db; _bridge = bridge; }

    public async Task<PagedResult<CampaignDto>> ListAsync(int ownerId, string? status, string? q, PageQuery page)
    {
        var query = _db.Campaigns.Include(c => c.Template).Include(c => c.Group).Where(c => c.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<CampaignStatus>(status, true, out var s)) query = query.Where(c => c.Status == s);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(c => c.Name.Contains(q));
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(c => c.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return new PagedResult<CampaignDto>(items.Select(Map).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<CampaignDto> GetAsync(int ownerId, int id) => Map(await Owned(ownerId, id));

    public async Task<CampaignDto> CreateAsync(int ownerId, CreateCampaignRequest req)
    {
        if (!Enum.TryParse<EngineType>(req.EngineType, true, out var engine)) throw new BusinessException("Invalid engine.");
        Enum.TryParse<RotationMode>(req.RotationMode, true, out var rot);
        var group = await _db.ContactGroups.FirstOrDefaultAsync(g => g.Id == req.GroupId && g.OwnerId == ownerId)
            ?? throw new NotFoundException("Group not found.");
        var c = new Campaign
        {
            OwnerId = ownerId, Name = req.Name, TemplateId = req.TemplateId, Body = req.Body, GroupId = req.GroupId,
            EngineType = engine, AccountId = req.AccountId, RotationMode = rot, ScheduledAt = req.ScheduledAt,
            WindowStart = ParseTime(req.WindowStart), WindowEnd = ParseTime(req.WindowEnd),
            SpintaxEnabled = req.SpintaxEnabled, PersonalizeMessage = req.PersonalizeMessage,
            MinDelaySec = req.Pacing.MinDelaySec, MaxDelaySec = req.Pacing.MaxDelaySec,
            BatchSize = req.Pacing.BatchSize, CooldownMinutes = req.Pacing.CooldownMinutes,
            Status = req.ScheduledAt.HasValue ? CampaignStatus.Scheduled : CampaignStatus.Draft,
            TotalRecipients = group.TotalContacts
        };
        _db.Campaigns.Add(c); await _db.SaveChangesAsync();
        return Map(c);
    }

    public async Task<CampaignDto> UpdateAsync(int ownerId, int id, CreateCampaignRequest req)
    {
        var c = await Owned(ownerId, id);
        if (c.Status is not (CampaignStatus.Draft or CampaignStatus.Paused or CampaignStatus.Scheduled))
            throw new BusinessException("Campaign can only be edited while Draft, Scheduled or Paused.");
        c.Name = req.Name; c.TemplateId = req.TemplateId; c.Body = req.Body;
        c.MinDelaySec = req.Pacing.MinDelaySec; c.MaxDelaySec = req.Pacing.MaxDelaySec;
        c.BatchSize = req.Pacing.BatchSize; c.CooldownMinutes = req.Pacing.CooldownMinutes;
        c.SpintaxEnabled = req.SpintaxEnabled; c.PersonalizeMessage = req.PersonalizeMessage;
        if (req.AccountId.HasValue) c.AccountId = req.AccountId;
        c.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Map(c);
    }

    public async Task DeleteAsync(int ownerId, int id)
    {
        var c = await Owned(ownerId, id);
        if (c.Status is not (CampaignStatus.Draft or CampaignStatus.Stopped))
            throw new BusinessException("Only Draft or Stopped campaigns can be deleted.");
        _db.Campaigns.Remove(c); await _db.SaveChangesAsync();
    }

    public async Task<CampaignDto> ScheduleAsync(int ownerId, int id, ScheduleRequest req)
    {
        var c = await Owned(ownerId, id);
        c.ScheduledAt = req.ScheduledAt; c.WindowStart = ParseTime(req.WindowStart); c.WindowEnd = ParseTime(req.WindowEnd);
        c.Status = CampaignStatus.Scheduled;
        await _db.SaveChangesAsync();
        return Map(c);
    }

    public async Task<CampaignDto> TransitionAsync(int ownerId, int id, string action)
    {
        var c = await Owned(ownerId, id);
        switch (action)
        {
            case "start":
                if (c.Status is not (CampaignStatus.Draft or CampaignStatus.Scheduled or CampaignStatus.Paused))
                    throw new BusinessException("Cannot start from current state.");
                await EnsureConnectedAsync(c);
                await EnsureRecipientsAsync(c);
                c.Status = CampaignStatus.Running; c.StartedAt ??= DateTime.UtcNow;
                break;
            case "pause":
                if (c.Status != CampaignStatus.Running) throw new BusinessException("Only running campaigns can be paused.");
                c.Status = CampaignStatus.Paused; break;
            case "resume":
                if (c.Status != CampaignStatus.Paused) throw new BusinessException("Only paused campaigns can be resumed.");
                c.Status = CampaignStatus.Running; break;
            case "stop":
                c.Status = CampaignStatus.Stopped; c.CompletedAt = DateTime.UtcNow; break;
            case "restart":
                // Re-send the same message to the same numbers: reset every recipient to
                // Pending, clear per-recipient results and the checkpoint, then run again.
                if (c.Status is CampaignStatus.Running)
                    throw new BusinessException("Stop or pause the campaign before restarting.");
                await EnsureConnectedAsync(c);
                await EnsureRecipientsAsync(c);
                await _db.CampaignRecipients.Where(r => r.CampaignId == c.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, RecipientStatus.Pending)
                    .SetProperty(r => r.SentAt, (DateTime?)null)
                    .SetProperty(r => r.DeliveredAt, (DateTime?)null)
                    .SetProperty(r => r.ReadAt, (DateTime?)null)
                    .SetProperty(r => r.Error, (string?)null)
                    .SetProperty(r => r.MessageId, (string?)null));
                var cp = await _db.CampaignCheckpoints.FirstOrDefaultAsync(x => x.CampaignId == c.Id);
                if (cp is not null) _db.CampaignCheckpoints.Remove(cp);
                c.ProcessedCount = 0; c.FailedCount = 0; c.CompletedAt = null;
                c.StartedAt = DateTime.UtcNow;
                c.Status = CampaignStatus.Running;
                break;
            default: throw new BusinessException("Unknown action.");
        }
        await _db.SaveChangesAsync();
        return Map(c);
    }

    /// <summary>
    /// Verifies a WhatsApp account is actually linked before a campaign can send.
    /// Baileys accounts must have a LIVE bridge session (QR scanned); if none is
    /// connected we block the start and tell the user to scan the QR. CloudApi uses
    /// stored Meta credentials, so only a credential check applies.
    /// </summary>
    private async Task EnsureConnectedAsync(Campaign c)
    {
        if (c.EngineType == EngineType.CloudApi)
        {
            var hasCreds = c.AccountId.HasValue
                ? await _db.WhatsAppAccounts.AnyAsync(a => a.Id == c.AccountId && a.CredentialsEnc != null)
                : await _db.WhatsAppAccounts.AnyAsync(a => a.OwnerId == c.OwnerId && a.EngineType == EngineType.CloudApi && a.CredentialsEnc != null);
            if (!hasCreds)
                throw new BusinessException("No WhatsApp Cloud API credentials configured. Add them on the account before sending.");
            return;
        }

        // Candidate Baileys accounts for this campaign.
        var accounts = c.AccountId.HasValue
            ? await _db.WhatsAppAccounts.Where(a => a.Id == c.AccountId && a.OwnerId == c.OwnerId).ToListAsync()
            : await _db.WhatsAppAccounts.Where(a => a.OwnerId == c.OwnerId && a.EngineType == EngineType.Baileys).ToListAsync();

        if (accounts.Count == 0)
            throw new BusinessException("No WhatsApp account for this campaign. Add and connect one first.");

        // A send is only real when the bridge reports a live "connected" session.
        var connectedId = 0;
        var bridgeReachable = false;
        foreach (var a in accounts)
        {
            var status = await _bridge.StatusAsync(a.Id);
            if (status is not null) bridgeReachable = true;
            if (string.Equals(status, "connected", StringComparison.OrdinalIgnoreCase)) { connectedId = a.Id; break; }
        }

        if (connectedId != 0)
        {
            // Keep the DB in sync with the live truth.
            foreach (var a in accounts.Where(a => a.Id == connectedId && a.Status != WaAccountStatus.Connected))
                a.Status = WaAccountStatus.Connected;
            return;
        }

        // Nothing is actually linked. Mark stale "Connected" rows as disconnected so the UI is honest.
        foreach (var a in accounts.Where(a => a.Status == WaAccountStatus.Connected))
            a.Status = WaAccountStatus.Disconnected;
        await _db.SaveChangesAsync();

        if (!bridgeReachable)
            throw new BusinessException("WhatsApp engine is offline. Open Accounts, connect a Baileys account and scan the QR, then try again.");
        throw new BusinessException("No WhatsApp account is linked. Open Accounts → Connect and scan the QR with your phone before starting the campaign.");
    }

    /// <summary>Materializes one recipient row per contact (idempotent — duplicate-safe).</summary>
    public async Task EnsureRecipientsAsync(Campaign c)
    {
        if (await _db.CampaignRecipients.AnyAsync(r => r.CampaignId == c.Id)) return;
        var contacts = await _db.Contacts.Where(x => x.GroupId == c.GroupId).ToListAsync();
        var recipients = contacts.Select(ct => new CampaignRecipient
        {
            CampaignId = c.Id, ContactId = ct.Id, Phone = ct.Phone, Name = ct.Name,
            PersonalizationJson = ct.CustomFields, Status = RecipientStatus.Pending
        }).ToList();
        // Chunk insert for large groups (50k+).
        foreach (var chunk in recipients.Chunk(2000))
        {
            _db.CampaignRecipients.AddRange(chunk);
            await _db.SaveChangesAsync();
        }
        c.TotalRecipients = recipients.Count;
        await _db.SaveChangesAsync();
    }

    public async Task<CampaignProgress> ProgressAsync(int ownerId, int id)
    {
        var c = await Owned(ownerId, id);
        var stats = await _db.CampaignRecipients.Where(r => r.CampaignId == id)
            .GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        int Count(RecipientStatus s) => stats.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        var total = stats.Sum(x => x.Count);
        var sent = Count(RecipientStatus.Sent); var delivered = Count(RecipientStatus.Delivered);
        var read = Count(RecipientStatus.Read); var failed = Count(RecipientStatus.Failed);
        var processed = sent + delivered + read + failed;
        var cp = await _db.CampaignCheckpoints.FirstOrDefaultAsync(x => x.CampaignId == id);
        var remaining = total - processed;
        var avgDelay = (c.MinDelaySec + c.MaxDelaySec) / 2.0;
        return new CampaignProgress(id, c.Status.ToString(), total, processed, sent, delivered, read, failed,
            cp?.LastRecipientId, remaining > 0 ? (int)(remaining * avgDelay) : 0, cp?.UpdatedAt);
    }

    public async Task<PagedResult<CampaignRecipientDto>> RecipientsAsync(int ownerId, int id, string? status, PageQuery page)
    {
        await Owned(ownerId, id);
        var query = _db.CampaignRecipients.Where(r => r.CampaignId == id);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RecipientStatus>(status, true, out var s)) query = query.Where(r => r.Status == s);
        var total = await query.CountAsync();
        var items = await query.OrderBy(r => r.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return new PagedResult<CampaignRecipientDto>(items.Select(r => r.ToDto()).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<(byte[] bytes, string contentType, string fileName)> ReportAsync(int ownerId, int id, string format)
    {
        var c = await Owned(ownerId, id);
        var recipients = await _db.CampaignRecipients.Where(r => r.CampaignId == id).ToListAsync();
        if (format.Equals("xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.Worksheets.Add("Report");
            string[] headers = { "Phone", "Name", "Status", "SentAt", "DeliveredAt", "ReadAt", "Error" };
            for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
            for (int i = 0; i < recipients.Count; i++)
            {
                var r = recipients[i];
                ws.Cell(i + 2, 1).Value = PhoneUtils.Mask(r.Phone); ws.Cell(i + 2, 2).Value = r.Name;
                ws.Cell(i + 2, 3).Value = r.Status.ToString(); ws.Cell(i + 2, 4).Value = r.SentAt?.ToString("u");
                ws.Cell(i + 2, 5).Value = r.DeliveredAt?.ToString("u"); ws.Cell(i + 2, 6).Value = r.ReadAt?.ToString("u");
                ws.Cell(i + 2, 7).Value = r.Error;
            }
            using var ms = new MemoryStream(); wb.SaveAs(ms);
            return (ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{c.Name}-report.xlsx");
        }
        var sb = new System.Text.StringBuilder("Phone,Name,Status,SentAt,DeliveredAt,ReadAt,Error\n");
        foreach (var r in recipients)
            sb.AppendLine($"{PhoneUtils.Mask(r.Phone)},{r.Name},{r.Status},{r.SentAt:u},{r.DeliveredAt:u},{r.ReadAt:u},{r.Error}");
        return (System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"{c.Name}-report.csv");
    }

    private async Task<Campaign> Owned(int ownerId, int id) =>
        await _db.Campaigns.Include(c => c.Template).Include(c => c.Group).FirstOrDefaultAsync(c => c.Id == id && c.OwnerId == ownerId)
        ?? throw new NotFoundException("Campaign not found.");

    private static TimeSpan? ParseTime(string? v) => TimeSpan.TryParse(v, out var t) ? t : null;

    public static CampaignDto Map(Campaign c)
    {
        var progress = c.TotalRecipients == 0 ? 0 : (int)(c.ProcessedCount * 100.0 / c.TotalRecipients);
        return new CampaignDto(c.Id, c.Name, c.TemplateId, c.Template?.Name, c.GroupId, c.Group?.Name,
            c.EngineType.ToString(), c.AccountId, c.RotationMode.ToString(), c.Status.ToString(), c.ScheduledAt,
            c.WindowStart?.ToString(), c.WindowEnd?.ToString(), c.SpintaxEnabled, c.PersonalizeMessage,
            new PacingDto(c.MinDelaySec, c.MaxDelaySec, c.BatchSize, c.CooldownMinutes),
            c.TotalRecipients, c.ProcessedCount, c.FailedCount, progress, c.StartedAt, c.CompletedAt, c.CreatedAt);
    }
}
