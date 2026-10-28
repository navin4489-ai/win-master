using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinMaster.Core;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Core.Services;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

/// <summary>
/// Executes running campaigns: personalization + spintax, human pacing, batch cooldown,
/// account rotation, and crash-safe resume from the last checkpoint (never re-sends a Sent row).
/// </summary>
public class CampaignWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CampaignWorker> _log;
    private readonly SpintaxService _spintax = new();
    private readonly PersonalizationService _personalize = new();

    public CampaignWorker(IServiceScopeFactory scopes, ILogger<CampaignWorker> log)
    { _scopes = scopes; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Campaign worker tick failed."); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IWhatsAppChannelFactory>();

        // Process every running campaign each tick (round-robin), so one campaign's
        // pacing can never starve the others.
        var running = await db.Campaigns.Where(c => c.Status == CampaignStatus.Running)
            .OrderBy(c => c.StartedAt).Select(c => c.Id).ToListAsync(ct);
        foreach (var id in running)
        {
            if (ct.IsCancellationRequested) return;
            try { await ProcessDueAsync(db, factory, id, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { _log.LogError(ex, "Campaign {Id} batch failed.", id); }
        }
    }

    /// <summary>
    /// Sends at most one recipient per tick for a campaign, honoring pacing via the
    /// checkpoint timestamp (no blocking Task.Delay). This keeps the worker responsive
    /// and prevents a single slow/hung send from freezing all campaigns.
    /// </summary>
    private async Task ProcessDueAsync(AppDbContext db, IWhatsAppChannelFactory factory, int campaignId, CancellationToken ct)
    {
        var c = await db.Campaigns.Include(x => x.Template).FirstOrDefaultAsync(x => x.Id == campaignId, ct);
        if (c is null || c.Status != CampaignStatus.Running) return;

        var cp = await db.CampaignCheckpoints.FirstOrDefaultAsync(x => x.CampaignId == campaignId, ct);
        if (cp is null) { cp = new CampaignCheckpoint { CampaignId = campaignId }; db.CampaignCheckpoints.Add(cp); await db.SaveChangesAsync(ct); }

        // Pacing gate: wait out the inter-message delay without blocking the thread.
        var waitSec = cp.NextSendDelaySec > 0 ? cp.NextSendDelaySec : 0;
        if (cp.UpdatedAt.HasValue && DateTime.UtcNow < cp.UpdatedAt.Value.AddSeconds(waitSec))
            return;

        var recipient = await db.CampaignRecipients
            .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending)
            .OrderBy(r => r.Id).FirstOrDefaultAsync(ct);
        if (recipient is null) { await CompleteAsync(db, c, ct); return; }

        var accounts = await ResolveAccountsAsync(db, c, ct);
        recipient.Status = RecipientStatus.Sending;
        await db.SaveChangesAsync(ct);

        var body = BuildMessage(c, recipient);
        recipient.VariantText = body;

        var channel = factory.For(c.EngineType);
        var accountId = accounts.Count == 0 ? (c.AccountId ?? 0) : accounts[cp.RotationIndex % accounts.Count];
        var result = await channel.SendAsync(accountId, new OutboundMessage(recipient.Phone, body, null, MediaType.None, null, null), ct);

        if (result.Success)
        {
            recipient.Status = RecipientStatus.Sent; recipient.SentAt = DateTime.UtcNow; recipient.MessageId = result.MessageId;
            c.ProcessedCount++;
        }
        else { recipient.Status = RecipientStatus.Failed; recipient.Error = result.Error; c.FailedCount++; c.ProcessedCount++; }

        cp.LastRecipientId = recipient.Id;
        cp.RotationIndex = c.RotationMode == RotationMode.RoundRobin ? cp.RotationIndex + 1 : cp.RotationIndex;
        cp.UpdatedAt = DateTime.UtcNow;
        // Schedule the next send after a randomized human delay (+ batch cooldown).
        var rng = new Random();
        var delay = rng.Next(c.MinDelaySec, Math.Max(c.MinDelaySec + 1, c.MaxDelaySec + 1));
        cp.SentInBatch++;
        if (c.BatchSize > 0 && cp.SentInBatch >= c.BatchSize && c.CooldownMinutes > 0)
        {
            delay = c.CooldownMinutes * 60;
            cp.SentInBatch = 0;
        }
        cp.NextSendDelaySec = delay;
        await db.SaveChangesAsync(ct);

        // If that was the last pending recipient, complete immediately.
        var anyLeft = await db.CampaignRecipients.AnyAsync(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending, ct);
        if (!anyLeft) await CompleteAsync(db, c, ct);
    }

    /// <summary>Processes a single pacing batch; exposed for integration tests (no delays).</summary>
    public async Task ProcessOneBatchAsync(AppDbContext db, IWhatsAppChannelFactory factory, int campaignId, CancellationToken ct, bool noDelay = false)
    {
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == campaignId, ct);
        if (c is null || c.Status != CampaignStatus.Running) return;

        var cp = await db.CampaignCheckpoints.FirstOrDefaultAsync(x => x.CampaignId == campaignId, ct);
        if (cp is null) { cp = new CampaignCheckpoint { CampaignId = campaignId }; db.CampaignCheckpoints.Add(cp); await db.SaveChangesAsync(ct); }

        var channel = factory.For(c.EngineType);
        var accounts = await ResolveAccountsAsync(db, c, ct);
        int sentThisBatch = 0;

        while (sentThisBatch < c.BatchSize)
        {
            if (ct.IsCancellationRequested) return;
            var recipient = await db.CampaignRecipients
                .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending && r.Id > cp.LastRecipientId)
                .OrderBy(r => r.Id).FirstOrDefaultAsync(ct);
            if (recipient is null)
            {
                recipient = await db.CampaignRecipients
                    .Where(r => r.CampaignId == campaignId && r.Status == RecipientStatus.Pending)
                    .OrderBy(r => r.Id).FirstOrDefaultAsync(ct);
            }
            if (recipient is null) { await CompleteAsync(db, c, ct); return; }

            recipient.Status = RecipientStatus.Sending;
            await db.SaveChangesAsync(ct);

            var body = BuildMessage(c, recipient);
            recipient.VariantText = body;

            var accountId = accounts.Count == 0 ? (c.AccountId ?? 0) : accounts[cp.RotationIndex % accounts.Count];
            var result = await channel.SendAsync(accountId, new OutboundMessage(recipient.Phone, body, null, MediaType.None, null, null), ct);

            if (result.Success)
            {
                recipient.Status = RecipientStatus.Sent; recipient.SentAt = DateTime.UtcNow; recipient.MessageId = result.MessageId;
                c.ProcessedCount++;
            }
            else { recipient.Status = RecipientStatus.Failed; recipient.Error = result.Error; c.FailedCount++; c.ProcessedCount++; }

            cp.LastRecipientId = recipient.Id; cp.SentInBatch = ++sentThisBatch;
            cp.RotationIndex = c.RotationMode == RotationMode.RoundRobin ? cp.RotationIndex + 1 : cp.RotationIndex;
            cp.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<List<int>> ResolveAccountsAsync(AppDbContext db, Campaign c, CancellationToken ct)
    {
        if (c.AccountId.HasValue) return new List<int> { c.AccountId.Value };
        return await db.WhatsAppAccounts
            .Where(a => a.OwnerId == c.OwnerId && a.EngineType == c.EngineType && a.Status == WaAccountStatus.Connected)
            .OrderBy(a => a.Id).Select(a => a.Id).ToListAsync(ct);
    }

    private static async Task CompleteAsync(AppDbContext db, Campaign c, CancellationToken ct)
    {
        c.Status = CampaignStatus.Completed; c.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public string BuildMessage(Campaign c, CampaignRecipient r)
    {
        var template = c.Body ?? "";
        if (c.TemplateId.HasValue)
        {
            // Template body is loaded lazily by callers that include it; fall back to inline body otherwise.
            template = c.Template?.Body ?? c.Body ?? template;
        }
        var text = template;
        if (c.PersonalizeMessage)
        {
            var fields = Mapping.ParseFields(r.PersonalizationJson);
            if (!string.IsNullOrEmpty(r.Name)) fields["Name"] = r.Name;
            text = _personalize.Apply(text, fields);
        }
        if (c.SpintaxEnabled) text = _spintax.Spin(text);
        return text;
    }
}

/// <summary>Promotes Scheduled campaigns to Running at their scheduled time / window.</summary>
public class SchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SchedulerService> _log;
    public SchedulerService(IServiceScopeFactory scopes, ILogger<SchedulerService> log) { _scopes = scopes; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var campaigns = scope.ServiceProvider.GetRequiredService<CampaignService>();
                var now = DateTime.UtcNow;
                var due = await db.Campaigns.Where(c => c.Status == CampaignStatus.Scheduled && c.ScheduledAt <= now).ToListAsync(ct);
                foreach (var c in due)
                {
                    if (c.WindowStart.HasValue && c.WindowEnd.HasValue)
                    {
                        var t = now.TimeOfDay;
                        if (t < c.WindowStart || t > c.WindowEnd) continue;
                    }
                    await campaigns.EnsureRecipientsAsync(c);
                    c.Status = CampaignStatus.Running; c.StartedAt ??= now;
                    await db.SaveChangesAsync(ct);
                    _log.LogInformation("Scheduled campaign {Id} started.", c.Id);
                }
            }
            catch (Exception ex) { _log.LogError(ex, "Scheduler tick failed."); }
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
        }
    }
}
