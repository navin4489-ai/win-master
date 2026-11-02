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
    private readonly IWaBridgeClient _bridge;
    private readonly SpintaxService _spintax = new();
    private readonly PersonalizationService _personalize = new();

    public CampaignWorker(IServiceScopeFactory scopes, ILogger<CampaignWorker> log, IWaBridgeClient bridge)
    { _scopes = scopes; _log = log; _bridge = bridge; }

    // Keeps direct unit-test construction compatible; production uses DI above.
    public CampaignWorker(IServiceScopeFactory scopes, ILogger<CampaignWorker> log)
        : this(scopes, log, new DisabledBridgeClient()) { }

    private sealed class DisabledBridgeClient : IWaBridgeClient
    {
        public Task<bool> ConnectAsync(int accountId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> DisconnectAsync(int accountId, bool wipe = false, CancellationToken ct = default) => Task.FromResult(true);
        public Task<string?> StatusAsync(int accountId, CancellationToken ct = default) => Task.FromResult<string?>("connected");
    }

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

        // Auto-resume any campaign whose safety pause has expired (daily cap rolled over,
        // or restriction cool-down elapsed) so nothing stays parked forever.
        await AutoResumeAsync(db, ct);

        // Process every running campaign each tick (round-robin), so one campaign's
        // pacing can never starve the others.
        var running = await db.Campaigns.Where(c => c.Status == CampaignStatus.Running || (c.Status == CampaignStatus.Paused && c.PausedByConnection))
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
    /// Flips a safety-paused campaign back to Running once its resume time has passed and the
    /// WhatsApp account is connected again. Daily-limit pauses resume at the next UTC day;
    /// restriction pauses resume after the cool-down. Connection pauses are handled by
    /// <see cref="EnsureLiveAsync"/>.
    /// </summary>
    internal async Task AutoResumeAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Self-heal: revert any recipient orphaned in "Sending" (e.g. a send was interrupted by a
        // dropped session, worker restart, or timeout) back to Pending so it is retried instead of
        // being silently lost. Only touch recipients not updated in the last 2 minutes to avoid
        // racing an in-flight send.
        var stuckCutoff = now.AddMinutes(-2);
        var stuck = await db.CampaignRecipients
            .Where(r => r.Status == RecipientStatus.Sending && r.UpdatedAt < stuckCutoff)
            .ToListAsync(ct);
        if (stuck.Count > 0)
        {
            foreach (var r in stuck) r.Status = RecipientStatus.Pending;
            await db.SaveChangesAsync(ct);
            _log.LogWarning("Reverted {Count} orphaned 'Sending' recipient(s) to Pending for retry.", stuck.Count);
        }

        var paused = await db.Campaigns
            .Where(c => c.Status == CampaignStatus.Paused && c.PausedByConnection && c.PauseReason != null)
            .ToListAsync(ct);
        foreach (var c in paused)
        {
            // Connection pauses resume via EnsureLiveAsync once the bridge is live again.
            if (c.PauseReason == "connection") continue;

            // A failure-streak pause needs human action (fix the contact list) → never auto-resume.
            if (c.PauseReason == "failures") continue;

            if (c.PauseReason == "daily_limit" && c.LimitResumeAt.HasValue && now < c.LimitResumeAt.Value)
                continue; // not yet the next day

            if (c.PauseReason == "restriction")
            {
                if (!c.RestrictionPausedAt.HasValue || now < AccountSafety.RestrictionResumeAt(c.RestrictionPausedAt.Value))
                    continue; // still cooling down
                // Cool-down elapsed: clear the account flag so we cautiously resume. If WhatsApp
                // is still limiting us, the very next send fails with a restriction error and we
                // re-pause immediately — safe, self-correcting probing.
                var accId = c.AccountId ?? 0;
                var acc = accId > 0 ? await db.WhatsAppAccounts.FindAsync(new object[] { accId }, ct) : null;
                if (acc != null) { acc.IsRestricted = false; acc.RestrictionReason = null; acc.RestrictedAt = null; }
            }

            var resumedFrom = c.PauseReason;
            c.Status = CampaignStatus.Running;
            c.PausedByConnection = false; c.PauseReason = null;
            c.ConnectionMessage = null; c.LimitResumeAt = null; c.RestrictionPausedAt = null;
            _log.LogInformation("Campaign {Id} auto-resumed from '{Reason}' pause.", c.Id, resumedFrom);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Sends at most one recipient per tick for a campaign, honoring pacing via the
    /// checkpoint timestamp (no blocking Task.Delay). This keeps the worker responsive
    /// and prevents a single slow/hung send from freezing all campaigns.
    /// </summary>
    internal async Task ProcessDueAsync(AppDbContext db, IWhatsAppChannelFactory factory, int campaignId, CancellationToken ct)
    {
        var c = await db.Campaigns.Include(x => x.Template).FirstOrDefaultAsync(x => x.Id == campaignId, ct);
        if (c is null || (c.Status != CampaignStatus.Running && !(c.Status == CampaignStatus.Paused && c.PausedByConnection))) return;

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

        // ---- Account safety gate: never let volume endanger the WhatsApp number ----
        // Resolved before the live-session gate so a RESTRICTED account pauses immediately
        // instead of being put into a reconnect loop (reconnecting can't send anyway).
        var accountId = accounts.Count == 0 ? (c.AccountId ?? 0) : accounts[cp.RotationIndex % accounts.Count];
        var acc = accountId > 0 ? await db.WhatsAppAccounts.FindAsync(new object[] { accountId }, ct) : null;
        var ban = acc != null ? await db.AntiBanSettings.FirstOrDefaultAsync(b => b.AccountId == acc.Id, ct) : null;
        var now = DateTime.UtcNow;

        if (acc != null)
        {
            // Roll the daily counter over on a new UTC day.
            if (AccountSafety.ShouldResetDaily(acc, now)) { AccountSafety.ResetDaily(acc, now); await db.SaveChangesAsync(ct); }

            // Already flagged as restricted → hard-stop, never send.
            if (acc.IsRestricted)
            {
                await PauseForRestrictionAsync(db, c, acc, acc.RestrictionReason, ct);
                return;
            }

            // Daily cap: pause the campaign (not fail) and auto-resume at the next day.
            var cap = AccountSafety.EffectiveDailyCap(acc, ban);
            if (acc.SentToday >= cap)
            {
                var resumeAt = now.Date.AddDays(1);
                c.Status = CampaignStatus.Paused; c.PausedByConnection = true; c.PauseReason = "daily_limit";
                c.LimitResumeAt = resumeAt; c.ConnectionMessage = AccountSafety.DailyCapMessage(acc.SentToday, cap, resumeAt);
                await db.SaveChangesAsync(ct);
                _log.LogWarning("Campaign {Id} paused at daily cap {Sent}/{Cap} for account {AccId}.", c.Id, acc.SentToday, cap, acc.Id);
                return;
            }
        }

        if (!await EnsureLiveAsync(db, c, accounts, ct)) return;

        recipient.Status = RecipientStatus.Sending;
        recipient.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var body = BuildMessage(c, recipient);
        recipient.VariantText = body;

        var channel = factory.For(c.EngineType);
        var result = await channel.SendAsync(accountId, BuildOutbound(c, recipient.Phone, body), ct);

        if (result.Success)
        {
            recipient.Status = RecipientStatus.Sent; recipient.SentAt = DateTime.UtcNow; recipient.MessageId = result.MessageId;
            c.ProcessedCount++;
            cp.ConsecutiveFailures = 0;
            if (acc != null) acc.SentToday++; // count toward the daily cap
        }
        else
        {
            // A "session not connected" error (e.g. bridge 404 — no live WhatsApp session) means
            // the link dropped even though the DB may still say "Connected". Treat it as a
            // connection drop: keep the recipient pending, mark the account disconnected, pause
            // the campaign and trigger reconnect/QR. Never burn the recipient on a dropped session.
            var notConnected = result.Error is not null &&
                result.Error.Contains("not connected", StringComparison.OrdinalIgnoreCase);

            var stillLive = true;
            if (c.EngineType == EngineType.Baileys)
                stillLive = !notConnected && string.Equals(await _bridge.StatusAsync(accountId, ct), "connected", StringComparison.OrdinalIgnoreCase);
            if (!stillLive)
            {
                recipient.Status = RecipientStatus.Pending;
                if (acc != null && acc.Status == WaAccountStatus.Connected) { acc.Status = WaAccountStatus.Disconnected; await db.SaveChangesAsync(ct); }
                await PauseForConnectionAsync(db, c, accounts, ct);
                return;
            }

            // EARLY RESTRICTION DETECTION: WhatsApp throttling/banning manifests as send
            // errors (rate-limit, quality, etc.) or a run of failures against live numbers.
            // Pause immediately instead of burning the rest of the list and deepening the ban.
            if (AccountSafety.IsRestrictionError(result.Error))
            {
                recipient.Status = RecipientStatus.Failed; recipient.Error = result.Error; c.FailedCount++; c.ProcessedCount++;
                cp.ConsecutiveFailures = 0;
                if (acc != null) { acc.IsRestricted = true; acc.RestrictionReason = result.Error; acc.RestrictedAt = DateTime.UtcNow; }
                await db.SaveChangesAsync(ct);
                await PauseForRestrictionAsync(db, c, acc, result.Error, ct);
                return;
            }

            recipient.Status = RecipientStatus.Failed; recipient.Error = result.Error; c.FailedCount++; c.ProcessedCount++;
            cp.ConsecutiveFailures++;
            // Failure-streak circuit breaker. A burst of failures stops the campaign so we don't
            // burn the whole list. But ordinary failures (invalid number / not on WhatsApp) are a
            // DATA problem, not an account-health problem — so we pause the CAMPAIGN without
            // flagging the account as restricted. The account is only flagged by a real throttle
            // error (handled above) or a dropped session. This prevents a bad contact list from
            // wrongly banning a healthy, connected number.
            if (AccountSafety.IsFailureStreak(cp.ConsecutiveFailures))
            {
                cp.ConsecutiveFailures = 0; // reset so a manual resume starts clean
                c.Status = CampaignStatus.Paused; c.PausedByConnection = true; c.PauseReason = "failures";
                c.ConnectionMessage = "Paused after several failed sends in a row. These look like invalid or non-WhatsApp numbers — check the contact list, then resume. Your WhatsApp number was NOT restricted.";
                await db.SaveChangesAsync(ct);
                _log.LogWarning("Campaign {Id} paused after a failure streak (likely bad numbers); account {AccId} left healthy.", c.Id, acc?.Id);
                return;
            }
        }

        cp.LastRecipientId = recipient.Id;
        cp.RotationIndex = c.RotationMode == RotationMode.RoundRobin ? cp.RotationIndex + 1 : cp.RotationIndex;
        cp.UpdatedAt = DateTime.UtcNow;
        // Schedule the next send using the SAFE pacing floor (never faster than anti-ban allows).
        var (minDelay, maxDelay) = acc != null
            ? AccountSafety.SafePacing(acc, ban, c.MinDelaySec, c.MaxDelaySec)
            : (c.MinDelaySec, Math.Max(c.MinDelaySec + 1, c.MaxDelaySec));
        var rng = new Random();
        var delay = rng.Next(minDelay, maxDelay + 1);
        cp.SentInBatch++;
        var batchSize = AccountSafety.SafeBatchSize(ban, c.BatchSize);
        var cooldownMin = AccountSafety.SafeCooldownMinutes(ban, c.CooldownMinutes);
        if (batchSize > 0 && cp.SentInBatch >= batchSize && cooldownMin > 0)
        {
            delay = cooldownMin * 60;
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
        if (c is null || (c.Status != CampaignStatus.Running && !(c.Status == CampaignStatus.Paused && c.PausedByConnection))) return;

        var cp = await db.CampaignCheckpoints.FirstOrDefaultAsync(x => x.CampaignId == campaignId, ct);
        if (cp is null) { cp = new CampaignCheckpoint { CampaignId = campaignId }; db.CampaignCheckpoints.Add(cp); await db.SaveChangesAsync(ct); }

        var channel = factory.For(c.EngineType);
        var accounts = await ResolveAccountsAsync(db, c, ct);
        if (!await EnsureLiveAsync(db, c, accounts, ct)) return;
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
            var result = await channel.SendAsync(accountId, BuildOutbound(c, recipient.Phone, body), ct);

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

    /// <summary>Live-session gate. Returns true when the campaign may send now.
    /// A disconnected Baileys campaign is paused (auto-retry) and returns false;
    /// a previously connection-paused campaign that is live again is auto-resumed.</summary>
    private async Task<bool> EnsureLiveAsync(AppDbContext db, Campaign c, List<int> accounts, CancellationToken ct)
    {
        if (c.EngineType != EngineType.Baileys) return true;

        var live = false;
        foreach (var candidate in accounts)
        {
            if (string.Equals(await _bridge.StatusAsync(candidate, ct), "connected", StringComparison.OrdinalIgnoreCase)) { live = true; break; }
        }

        if (!live)
        {
            // The bridge reports not-connected. But if the account is LINKED (DB status Connected
            // and it has persisted credentials), the in-memory session was almost certainly just
            // dropped by a free-tier restart and only needs to rehydrate. Kick a reconnect and
            // give it a short grace window instead of immediately pausing with "disconnected" —
            // this is the fix for "account connected but campaign says disconnected".
            var linked = await db.WhatsAppAccounts
                .Where(a => accounts.Contains(a.Id) && a.Status == WaAccountStatus.Connected)
                .Select(a => a.Id).ToListAsync(ct);
            var hasCreds = linked.Count > 0 && await db.WaSessionStates
                .AnyAsync(s => linked.Contains(s.AccountId) && s.Key == "creds", ct);

            if (hasCreds)
            {
                foreach (var accountId in linked)
                {
                    var status = await _bridge.StatusAsync(accountId, ct);
                    if (!string.Equals(status, "connecting", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(status, "qr", StringComparison.OrdinalIgnoreCase))
                    {
                        await _bridge.ConnectAsync(accountId, ct); // wake the restore from saved creds
                    }
                }
                // Brief grace: re-check once after giving the socket a moment to come up.
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                foreach (var candidate in linked)
                {
                    if (string.Equals(await _bridge.StatusAsync(candidate, ct), "connected", StringComparison.OrdinalIgnoreCase)) { live = true; break; }
                }
            }
        }

        if (!live)
        {
            await PauseForConnectionAsync(db, c, accounts, ct);
            return false;
        }
        if (c.PausedByConnection || c.Status == CampaignStatus.Paused)
        {
            c.Status = CampaignStatus.Running;
            c.PausedByConnection = false;
            c.PauseReason = null; c.LimitResumeAt = null; c.RestrictionPausedAt = null;
            c.ConnectionMessage = null;
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    private async Task PauseForConnectionAsync(AppDbContext db, Campaign c, List<int> accounts, CancellationToken ct)
    {
        var wasAlreadyPaused = c.Status == CampaignStatus.Paused && c.PausedByConnection && c.PauseReason == "connection";
        c.Status = CampaignStatus.Paused;
        c.PausedByConnection = true;
        c.PauseReason = "connection";
        c.ConnectionMessage = "WhatsApp disconnected. Scan the QR in Accounts to resume.";
        await db.SaveChangesAsync(ct);

        // Kick a reconnect ONLY on the transition into the pause, and only for accounts that are
        // actually idle/disconnected. Re-kicking every tick (every 2s) — or while a QR handshake
        // is already in flight ("connecting"/"qr") — would restart the socket and prevent the QR
        // from ever stabilising. A manual Connect from the UI then also works without being churned.
        if (!wasAlreadyPaused)
        {
            foreach (var accountId in accounts)
            {
                var status = await _bridge.StatusAsync(accountId, ct);
                if (!string.Equals(status, "connecting", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(status, "qr", StringComparison.OrdinalIgnoreCase))
                {
                    await _bridge.ConnectAsync(accountId, ct);
                }
            }
            _log.LogWarning("Campaign {CampaignId} paused because WhatsApp is disconnected.", c.Id);
        }
    }

    /// <summary>
    /// Parks a campaign because WhatsApp is restricting/limiting the account. Sending stops
    /// immediately to avoid deepening the ban; the campaign auto-resumes after the cool-down
    /// (handled by <see cref="AutoResumeAsync"/>) once the account is flagged healthy again.
    /// </summary>
    private async Task PauseForRestrictionAsync(AppDbContext db, Campaign c, WhatsAppAccount? acc, string? reason, CancellationToken ct)
    {
        c.Status = CampaignStatus.Paused;
        c.PausedByConnection = true;
        c.PauseReason = "restriction";
        c.RestrictionPausedAt = acc?.RestrictedAt ?? DateTime.UtcNow;
        c.ConnectionMessage = AccountSafety.RestrictionMessage(reason);
        await db.SaveChangesAsync(ct);
        _log.LogWarning("Campaign {CampaignId} paused for account restriction: {Reason}", c.Id, reason ?? "unknown");
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

    private static OutboundMessage BuildOutbound(Campaign c, string phone, string body)
    {
        var mediaType = c.Template?.MediaType ?? MediaType.None;
        return new OutboundMessage(phone, body, c.Template?.MediaUrl, mediaType, c.Template?.Buttons, c.Template?.ListMenu);
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
