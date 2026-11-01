using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WinMaster.Core;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Infrastructure.Data;
using WinMaster.Infrastructure.Services;
using Xunit;

namespace WinMaster.Tests;

/// <summary>Proves the campaign worker enforces account safety: it pauses (not fails) at the
/// daily cap, stops immediately on a WhatsApp restriction signal or a failure streak, and
/// auto-resumes once the safe window has passed — so a number is never driven into a ban.</summary>
public class CampaignSafetyEnforcementTests
{
    private static AppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class FakeBridge : IWaBridgeClient
    {
        public string Status = "connected";
        public Task<bool> ConnectAsync(int accountId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DisconnectAsync(int accountId, bool wipe = false, CancellationToken ct = default) => Task.FromResult(true);
        public Task<string?> StatusAsync(int accountId, CancellationToken ct = default) => Task.FromResult<string?>(Status);
    }

    /// <summary>Channel that returns a controllable error for every send.</summary>
    private sealed class ErrorChannel : IWhatsAppChannel
    {
        public EngineType Engine => EngineType.Baileys;
        public string? Error;                       // null → success
        public Task<SendResult> SendAsync(int accountId, OutboundMessage m, CancellationToken ct = default)
            => Task.FromResult(Error is null
                ? new SendResult(true, Guid.NewGuid().ToString("N"), null)
                : new SendResult(false, null, Error));
    }

    private sealed class Factory : IWhatsAppChannelFactory
    {
        private readonly IWhatsAppChannel _c;
        public Factory(IWhatsAppChannel c) => _c = c;
        public IWhatsAppChannel For(EngineType e) => _c;
    }

    private sealed class DummyScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => throw new NotImplementedException();
    }

    private static (Campaign c, WhatsAppAccount acc) Seed(AppDbContext db, int recipients, int cap, int batch = 5)
    {
        db.ContactGroups.Add(new ContactGroup { Id = 1, OwnerId = 1, Name = "G" });
        var acc = new WhatsAppAccount
        {
            Id = 10, OwnerId = 1, EngineType = EngineType.Baileys, DisplayName = "Line",
            Status = WaAccountStatus.Connected, WarmupState = WarmupState.Ready,
            DailyLimit = 1000, SentToday = 0, LastDailyResetAt = DateTime.UtcNow
        };
        db.WhatsAppAccounts.Add(acc);
        db.AntiBanSettings.Add(new AccountAntiBanSettings { AccountId = 10, MinDelaySec = 0, MaxDelaySec = 1, BatchSize = batch, CooldownMinutes = 0, DailyCap = cap });
        db.SaveChanges();
        var c = new Campaign
        {
            Id = 1, OwnerId = 1, Name = "C", GroupId = 1, EngineType = EngineType.Baileys, AccountId = 10,
            Status = CampaignStatus.Running, Body = "Hi", SpintaxEnabled = false, PersonalizeMessage = false,
            MinDelaySec = 0, MaxDelaySec = 1, BatchSize = batch, CooldownMinutes = 0
        };
        db.Campaigns.Add(c);
        db.SaveChanges();
        for (int i = 1; i <= recipients; i++)
            db.CampaignRecipients.Add(new CampaignRecipient { CampaignId = c.Id, ContactId = i, Phone = $"+155501000{i:00}" });
        db.SaveChanges();
        return (c, acc);
    }

    private static CampaignWorker NewWorker(FakeBridge bridge) =>
        new(new DummyScopeFactory(), NullLogger<CampaignWorker>.Instance, bridge);

    private static void OpenPacingGate(AppDbContext db, int campaignId)
    {
        var cp = db.CampaignCheckpoints.FirstOrDefault(x => x.CampaignId == campaignId);
        if (cp is null) return; // checkpoint not created until the first send attempt
        cp.NextSendDelaySec = 0;
        db.SaveChanges();
    }

    // ---- Daily cap: pause (don't keep sending) once the account hits its limit ----

    [Fact]
    public async Task PausesAtDailyCap_AndSchedulesResumeForNextDay()
    {
        using var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        using var db = NewDb(conn);
        var (c, acc) = Seed(db, recipients: 10, cap: 5);
        acc.SentToday = 5; // already at the cap
        db.SaveChanges();

        var worker = NewWorker(new FakeBridge());
        await worker.ProcessDueAsync(db, new Factory(new ErrorChannel()), c.Id, CancellationToken.None);

        var after = await db.Campaigns.FirstAsync(x => x.Id == c.Id);
        Assert.Equal(CampaignStatus.Paused, after.Status);
        Assert.True(after.PausedByConnection);
        Assert.Equal("daily_limit", after.PauseReason);
        Assert.NotNull(after.LimitResumeAt);
        Assert.True(after.LimitResumeAt > DateTime.UtcNow, "resume must be scheduled in the future");
        Assert.Contains("daily safe limit", after.ConnectionMessage, StringComparison.OrdinalIgnoreCase);
        // No recipient should have been consumed.
        Assert.Equal(0, await db.CampaignRecipients.CountAsync(r => r.CampaignId == c.Id && r.Status != RecipientStatus.Pending));
    }

    // ---- Restriction signal: stop immediately, flag the account, park the campaign ----

    [Fact]
    public async Task RestrictionError_PausesImmediately_AndFlagsAccount()
    {
        using var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        using var db = NewDb(conn);
        var (c, acc) = Seed(db, recipients: 10, cap: 100);

        var worker = NewWorker(new FakeBridge());
        var channel = new ErrorChannel { Error = "Account temporarily blocked for spam" };
        await worker.ProcessDueAsync(db, new Factory(channel), c.Id, CancellationToken.None);

        var after = await db.Campaigns.FirstAsync(x => x.Id == c.Id);
        Assert.Equal(CampaignStatus.Paused, after.Status);
        Assert.Equal("restriction", after.PauseReason);
        Assert.NotNull(after.RestrictionPausedAt);

        var accAfter = await db.WhatsAppAccounts.FirstAsync(a => a.Id == acc.Id);
        Assert.True(accAfter.IsRestricted, "account must be flagged restricted");
        // Only the single offending recipient was attempted, then we stopped.
        Assert.Equal(1, await db.CampaignRecipients.CountAsync(r => r.CampaignId == c.Id && r.Status == RecipientStatus.Failed));
    }

    // ---- Failure-streak circuit breaker ----

    [Fact]
    public async Task ConsecutiveFailureStreak_PausesCampaign()
    {
        using var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        using var db = NewDb(conn);
        var (c, _) = Seed(db, recipients: 50, cap: 100);

        var worker = NewWorker(new FakeBridge());
        var channel = new ErrorChannel { Error = "invalid number" }; // ordinary failure, not a restriction
        var factory = new Factory(channel);

        for (int i = 0; i < AccountSafety.MaxConsecutiveFailures; i++)
        {
            OpenPacingGate(db, c.Id);
            await worker.ProcessDueAsync(db, factory, c.Id, CancellationToken.None);
        }

        var after = await db.Campaigns.FirstAsync(x => x.Id == c.Id);
        Assert.Equal(CampaignStatus.Paused, after.Status);
        Assert.Equal("restriction", after.PauseReason);
        Assert.Contains("consecutive send failures", after.ConnectionMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ---- A successful send resets the failure streak ----

    [Fact]
    public async Task SuccessResetsFailureStreak_SoNoFalsePause()
    {
        using var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        using var db = NewDb(conn);
        var (c, _) = Seed(db, recipients: 50, cap: 100);

        var worker = NewWorker(new FakeBridge());
        var factory = new Factory(new ErrorChannel { Error = "invalid number" });
        // Trip the streak to one less than the threshold, then succeed.
        for (int i = 0; i < AccountSafety.MaxConsecutiveFailures - 1; i++)
        {
            OpenPacingGate(db, c.Id);
            await worker.ProcessDueAsync(db, factory, c.Id, CancellationToken.None);
        }
        OpenPacingGate(db, c.Id);
        await worker.ProcessDueAsync(db, new Factory(new ErrorChannel { Error = null }), c.Id, CancellationToken.None);

        var cp = await db.CampaignCheckpoints.FirstAsync(x => x.CampaignId == c.Id);
        Assert.Equal(0, cp.ConsecutiveFailures);
        Assert.Equal(CampaignStatus.Running, (await db.Campaigns.FirstAsync(x => x.Id == c.Id)).Status);
    }

    // ---- Auto-resume ----

    [Fact]
    public async Task AutoResume_DailyLimit_ResumesAfterResumeTime()
    {
        using var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        using var db = NewDb(conn);
        var (c, _) = Seed(db, recipients: 5, cap: 5);
        c.Status = CampaignStatus.Paused; c.PausedByConnection = true; c.PauseReason = "daily_limit";
        c.LimitResumeAt = DateTime.UtcNow.AddMinutes(-5); // already past
        db.SaveChanges();

        await NewWorker(new FakeBridge()).AutoResumeAsync(db, CancellationToken.None);

        var after = await db.Campaigns.FirstAsync(x => x.Id == c.Id);
        Assert.Equal(CampaignStatus.Running, after.Status);
        Assert.Null(after.PauseReason);
        Assert.False(after.PausedByConnection);
    }

    [Fact]
    public async Task AutoResume_DailyLimit_BlocksBeforeResumeTime()
    {
        using var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        using var db = NewDb(conn);
        var (c, _) = Seed(db, recipients: 5, cap: 5);
        c.Status = CampaignStatus.Paused; c.PausedByConnection = true; c.PauseReason = "daily_limit";
        c.LimitResumeAt = DateTime.UtcNow.AddHours(5); // not yet
        db.SaveChanges();

        await NewWorker(new FakeBridge()).AutoResumeAsync(db, CancellationToken.None);

        Assert.Equal(CampaignStatus.Paused, (await db.Campaigns.FirstAsync(x => x.Id == c.Id)).Status);
    }

    [Fact]
    public async Task AutoResume_Restriction_RespectsCooldown_AndClearsAccountFlag()
    {
        using var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        using var db = NewDb(conn);
        var (c, acc) = Seed(db, recipients: 5, cap: 100);
        c.Status = CampaignStatus.Paused; c.PausedByConnection = true; c.PauseReason = "restriction";
        c.RestrictionPausedAt = DateTime.UtcNow.AddHours(-1); // only 1h ago, cooldown is 6h
        acc.IsRestricted = true;
        db.SaveChanges();

        var worker = NewWorker(new FakeBridge());

        // Still cooling down → stays paused, account still flagged.
        await worker.AutoResumeAsync(db, CancellationToken.None);
        Assert.Equal(CampaignStatus.Paused, (await db.Campaigns.FirstAsync(x => x.Id == c.Id)).Status);
        Assert.True((await db.WhatsAppAccounts.FirstAsync(a => a.Id == acc.Id)).IsRestricted);

        // Past the cooldown → resumes and clears the account flag.
        c.RestrictionPausedAt = DateTime.UtcNow.AddHours(-7);
        db.SaveChanges();
        await worker.AutoResumeAsync(db, CancellationToken.None);
        Assert.Equal(CampaignStatus.Running, (await db.Campaigns.FirstAsync(x => x.Id == c.Id)).Status);
        Assert.False((await db.WhatsAppAccounts.FirstAsync(a => a.Id == acc.Id)).IsRestricted);
    }

    [Fact]
    public async Task AutoResume_NeverTouchesManualPause()
    {
        using var conn = new SqliteConnection("DataSource=:memory:"); conn.Open();
        using var db = NewDb(conn);
        var (c, _) = Seed(db, recipients: 5, cap: 5);
        c.Status = CampaignStatus.Paused; c.PausedByConnection = false; c.PauseReason = null; // manual pause
        db.SaveChanges();

        await NewWorker(new FakeBridge()).AutoResumeAsync(db, CancellationToken.None);

        Assert.Equal(CampaignStatus.Paused, (await db.Campaigns.FirstAsync(x => x.Id == c.Id)).Status);
    }
}
