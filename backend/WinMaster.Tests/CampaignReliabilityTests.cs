using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WinMaster.Core;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Core.Services;
using WinMaster.Infrastructure.Data;
using WinMaster.Infrastructure.Services;
using Xunit;

namespace WinMaster.Tests;

/// <summary>Covers the reliability/ops batch: connection-aware pause/resume, failed-only
/// retry, +91 normalization, template media propagation and user-wise admin reporting.</summary>
public class CampaignReliabilityTests
{
    private static AppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>Bridge stub whose live status can be flipped to simulate a dropped session.</summary>
    private sealed class FakeBridge : IWaBridgeClient
    {
        public string Status = "connected";
        public int ConnectCalls;
        public Task<bool> ConnectAsync(int accountId, CancellationToken ct = default) { ConnectCalls++; return Task.FromResult(true); }
        public Task<bool> DisconnectAsync(int accountId, bool wipe = false, CancellationToken ct = default) => Task.FromResult(true);
        public Task<string?> StatusAsync(int accountId, CancellationToken ct = default) => Task.FromResult<string?>(Status);
    }

    private sealed class Channel : IWhatsAppChannel
    {
        public EngineType Engine => EngineType.Baileys;
        public readonly List<OutboundMessage> Sent = new();
        public bool Succeed = true;
        public Task<SendResult> SendAsync(int accountId, OutboundMessage m, CancellationToken ct = default)
        {
            Sent.Add(m);
            return Task.FromResult(Succeed
                ? new SendResult(true, Guid.NewGuid().ToString("N"), null)
                : new SendResult(false, null, "invalid number"));
        }
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

    private static Campaign NewCampaign(AppDbContext db, int recipients, MediaType mediaType = MediaType.None, string? mediaUrl = null)
    {
        db.ContactGroups.Add(new ContactGroup { Id = 1, OwnerId = 1, Name = "G" });
        db.WhatsAppAccounts.Add(new WhatsAppAccount { Id = 10, OwnerId = 1, EngineType = EngineType.Baileys, DisplayName = "Line", Status = WaAccountStatus.Connected });
        db.SaveChanges();
        var c = new Campaign
        {
            OwnerId = 1, Name = "C", GroupId = 1, EngineType = EngineType.Baileys, AccountId = 10,
            Status = CampaignStatus.Running, Body = "Hi {{Name}}", SpintaxEnabled = false, PersonalizeMessage = true,
            MinDelaySec = 0, MaxDelaySec = 0, BatchSize = 5, CooldownMinutes = 0
        };
        db.Campaigns.Add(c);
        db.SaveChanges();
        for (int i = 1; i <= recipients; i++)
            db.CampaignRecipients.Add(new CampaignRecipient { CampaignId = c.Id, ContactId = i, Phone = $"+155501000{i:00}", Name = $"U{i}" });
        c.TotalRecipients = recipients;
        if (mediaUrl is not null)
        {
            db.Templates.Add(new Template { OwnerId = 1, Name = "T", Body = "Hi", MediaType = mediaType, MediaUrl = mediaUrl });
            db.SaveChanges();
            c.TemplateId = db.Templates.OrderBy(t => t.Id).Last().Id;
        }
        db.SaveChanges();
        return c;
    }

    [Fact]
    public async Task Disconnect_PausesCampaign_AndReconnect_Resumes()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var c = NewCampaign(db, 5);

        var bridge = new FakeBridge { Status = "disconnected" };
        var worker = new CampaignWorker(new DummyScopeFactory(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CampaignWorker>.Instance, bridge);
        var channel = new Channel();
        var factory = new Factory(channel);

        // Session is down: nothing is sent and the campaign is paused with a helpful message.
        await worker.ProcessOneBatchAsync(db, factory, c.Id, CancellationToken.None, noDelay: true);
        Assert.Empty(channel.Sent);
        var paused = await db.Campaigns.FindAsync(c.Id);
        Assert.Equal(CampaignStatus.Paused, paused!.Status);
        Assert.True(paused.PausedByConnection);
        Assert.False(string.IsNullOrWhiteSpace(paused.ConnectionMessage));
        Assert.True(bridge.ConnectCalls > 0); // bridge asked to reconnect / emit QR

        // Session comes back: campaign auto-resumes and sends continue without duplicates.
        bridge.Status = "connected";
        await worker.ProcessOneBatchAsync(db, factory, c.Id, CancellationToken.None, noDelay: true);
        var resumed = await db.Campaigns.FindAsync(c.Id);
        Assert.Equal(CampaignStatus.Running, resumed!.Status);
        Assert.False(resumed.PausedByConnection);
        Assert.Equal(5, channel.Sent.Count);
        Assert.Equal(channel.Sent.Count, channel.Sent.Select(m => m.ToPhone).Distinct().Count());
    }

    [Fact]
    public async Task RetryFailed_ResetsOnlyFailed_KeepsSuccessful()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var c = NewCampaign(db, 4);
        c.Status = CampaignStatus.Stopped;
        db.SaveChanges();

        // Mark 2 sent, 2 failed.
        var rows = await db.CampaignRecipients.Where(r => r.CampaignId == c.Id).OrderBy(r => r.Id).ToListAsync();
        rows[0].Status = RecipientStatus.Sent; rows[0].SentAt = DateTime.UtcNow;
        rows[1].Status = RecipientStatus.Delivered; rows[1].SentAt = DateTime.UtcNow;
        rows[2].Status = RecipientStatus.Failed; rows[2].Error = "boom";
        rows[3].Status = RecipientStatus.Failed; rows[3].Error = "boom2";
        c.ProcessedCount = 4; c.FailedCount = 2;
        db.SaveChanges();

        var bridge = new FakeBridge();
        var svc = new CampaignService(db, bridge);
        var dto = await svc.RetryFailedAsync(1, c.Id);

        var after = NewDb(conn);
        var rows2 = await after.CampaignRecipients.Where(r => r.CampaignId == c.Id).OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(RecipientStatus.Sent, rows2[0].Status);       // preserved
        Assert.Equal(RecipientStatus.Delivered, rows2[1].Status);  // preserved
        Assert.Equal(RecipientStatus.Pending, rows2[2].Status);    // retried
        Assert.Equal(RecipientStatus.Pending, rows2[3].Status);    // retried
        Assert.Null(rows2[2].Error);
        var camp2 = await after.Campaigns.FindAsync(c.Id);
        Assert.Equal(CampaignStatus.Running, camp2!.Status);
        Assert.Equal(0, camp2.FailedCount);
        Assert.Equal(2, camp2.ProcessedCount); // only the two kept rows count as processed
    }

    [Fact]
    public async Task RetryFailed_WithNoFailures_Throws()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var c = NewCampaign(db, 2);
        c.Status = CampaignStatus.Stopped;
        db.SaveChanges();
        var rows = await db.CampaignRecipients.Where(r => r.CampaignId == c.Id).ToListAsync();
        foreach (var r in rows) { r.Status = RecipientStatus.Sent; r.SentAt = DateTime.UtcNow; }
        db.SaveChanges();

        var svc = new CampaignService(db, new FakeBridge());
        await Assert.ThrowsAsync<BusinessException>(() => svc.RetryFailedAsync(1, c.Id));
    }

    [Fact]
    public async Task TemplateMedia_IsPropagatedToOutboundMessage()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);
        var c = NewCampaign(db, 2, MediaType.Image, "https://cdn.example.com/pic.jpg");

        var worker = new CampaignWorker(new DummyScopeFactory(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CampaignWorker>.Instance, new FakeBridge());
        var channel = new Channel();
        await worker.ProcessOneBatchAsync(db, new Factory(channel), c.Id, CancellationToken.None, noDelay: true);

        Assert.Equal(2, channel.Sent.Count);
        Assert.All(channel.Sent, m =>
        {
            Assert.Equal("https://cdn.example.com/pic.jpg", m.MediaUrl);
            Assert.Equal(MediaType.Image, m.MediaType);
        });
    }

    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("09876543210", "+919876543210")]
    [InlineData("+919876543210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    [InlineData("00919876543210", "+919876543210")]
    public void IndianPhone_GetsCountryCode_Automatically(string input, string expected)
        => Assert.Equal(expected, PhoneUtils.NormalizeIndiaDefault(input));

    [Fact]
    public async Task UsersReport_AggregatesPerUser()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);

        db.Users.Add(new User { Id = 1, Email = "a@x.io", DisplayName = "A", Role = UserRole.User, PasswordHash = "h" });
        db.Users.Add(new User { Id = 2, Email = "b@x.io", DisplayName = "B", Role = UserRole.User, PasswordHash = "h" });
        db.ContactGroups.Add(new ContactGroup { Id = 1, OwnerId = 1, Name = "G" });
        db.SaveChanges();

        var c = new Campaign { OwnerId = 1, Name = "C1", GroupId = 1, EngineType = EngineType.Baileys, Status = CampaignStatus.Completed, TotalRecipients = 2 };
        db.Campaigns.Add(c);
        db.SaveChanges();
        db.CampaignRecipients.Add(new CampaignRecipient { CampaignId = c.Id, Phone = "+1555010001", Name = "U1", Status = RecipientStatus.Delivered, SentAt = DateTime.UtcNow });
        db.CampaignRecipients.Add(new CampaignRecipient { CampaignId = c.Id, Phone = "+1555010002", Name = "U2", Status = RecipientStatus.Failed, SentAt = DateTime.UtcNow });
        db.LeadProfiles.Add(new LeadProfile { OwnerId = 1, DisplayName = "L", Channel = CommunicationChannel.WhatsApp, IsConverted = true, ConversionValue = 500 });
        db.SaveChanges();

        var svc = new ReportService(db);
        var report = await svc.UsersReportAsync(null, null);

        var a = report.Single(r => r.UserId == 1);
        Assert.Equal(1, a.CampaignsCreated);
        Assert.Equal(1, a.CampaignsCompleted);
        Assert.Equal(2, a.MessagesProcessed);
        Assert.Equal(1, a.MessagesDelivered);
        Assert.Equal(1, a.MessagesFailed);
        Assert.Equal(50, a.DeliveryRate);
        Assert.Equal(1, a.ConvertedLeads);
        Assert.Equal(500, a.ConversionValue);

        var b = report.Single(r => r.UserId == 2);
        Assert.Equal(0, b.CampaignsCreated);
        Assert.Equal(0, b.MessagesProcessed);
    }
}
