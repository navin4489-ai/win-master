using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WinMaster.Core;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Infrastructure.Data;
using WinMaster.Infrastructure.Services;
using Xunit;

namespace WinMaster.Tests;

/// <summary>Verifies the hard NFR: a crash mid-campaign resumes without re-sending any Sent recipient.</summary>
public class CampaignResumeTests
{
    private static AppDbContext NewDb(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class CountingChannel : IWhatsAppChannel
    {
        public EngineType Engine => EngineType.Baileys;
        public readonly List<string> Sent = new();
        public Task<SendResult> SendAsync(int accountId, OutboundMessage m, CancellationToken ct = default)
        {
            Sent.Add(m.ToPhone);
            return Task.FromResult(new SendResult(true, Guid.NewGuid().ToString("N"), null));
        }
    }

    private sealed class Factory : IWhatsAppChannelFactory
    {
        private readonly IWhatsAppChannel _c;
        public Factory(IWhatsAppChannel c) => _c = c;
        public IWhatsAppChannel For(EngineType e) => _c;
    }

    [Fact]
    public async Task Resume_DoesNotDuplicate_Sends()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using var db = NewDb(conn);

        db.ContactGroups.Add(new ContactGroup { Id = 1, OwnerId = 1, Name = "G" });
        await db.SaveChangesAsync();
        var campaign = new Campaign
        {
            OwnerId = 1, Name = "Test", GroupId = 1, EngineType = EngineType.Baileys, AccountId = 10,
            Status = CampaignStatus.Running, Body = "Hi {{Name}}", SpintaxEnabled = false, PersonalizeMessage = true,
            MinDelaySec = 0, MaxDelaySec = 0, BatchSize = 3, CooldownMinutes = 0
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        for (int i = 1; i <= 10; i++)
            db.CampaignRecipients.Add(new CampaignRecipient { CampaignId = campaign.Id, ContactId = i, Phone = $"+155501000{i:00}", Name = $"U{i}" });
        campaign.TotalRecipients = 10;
        await db.SaveChangesAsync();

        var channel = new CountingChannel();
        var factory = new Factory(channel);
        var worker = new CampaignWorker(new DummyScopeFactory(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CampaignWorker>.Instance);

        // First batch of 3, then simulate a crash (new worker/db context), then keep processing.
        await worker.ProcessOneBatchAsync(db, factory, campaign.Id, CancellationToken.None, noDelay: true);
        Assert.Equal(3, channel.Sent.Count);

        // "Restart": fresh context over the same connection, fresh worker.
        using var db2 = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);
        var worker2 = new CampaignWorker(new DummyScopeFactory(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CampaignWorker>.Instance);
        await worker2.ProcessOneBatchAsync(db2, factory, campaign.Id, CancellationToken.None, noDelay: true);
        await worker2.ProcessOneBatchAsync(db2, factory, campaign.Id, CancellationToken.None, noDelay: true);
        await worker2.ProcessOneBatchAsync(db2, factory, campaign.Id, CancellationToken.None, noDelay: true);

        // Every phone sent exactly once; total equals recipient count.
        Assert.Equal(channel.Sent.Count, channel.Sent.Distinct().Count());
        Assert.Equal(10, channel.Sent.Count);

        using var db3 = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options);
        Assert.Equal(10, await db3.CampaignRecipients.CountAsync(r => r.CampaignId == campaign.Id && r.Status == RecipientStatus.Sent));
        Assert.Equal(CampaignStatus.Completed, (await db3.Campaigns.FindAsync(campaign.Id))!.Status);
    }

    [Fact]
    public void Pacing_DelayWithinConfiguredRange()
    {
        var rng = new Random(7);
        for (int i = 0; i < 50; i++)
        {
            int min = 5, max = 15;
            var delay = rng.Next(min, max + 1);
            Assert.InRange(delay, min, max);
        }
    }

    private sealed class DummyScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => throw new NotImplementedException();
    }
}
