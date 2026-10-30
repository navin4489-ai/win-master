using Microsoft.EntityFrameworkCore;
using WinMaster.Core.Entities;

namespace WinMaster.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Activation> Activations => Set<Activation>();
    public DbSet<Proxy> Proxies => Set<Proxy>();
    public DbSet<WhatsAppAccount> WhatsAppAccounts => Set<WhatsAppAccount>();
    public DbSet<WaSessionState> WaSessionStates => Set<WaSessionState>();
    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<CampaignCheckpoint> CampaignCheckpoints => Set<CampaignCheckpoint>();
    public DbSet<AccountAntiBanSettings> AntiBanSettings => Set<AccountAntiBanSettings>();
    public DbSet<AutoReplyRule> AutoReplyRules => Set<AutoReplyRule>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();
    public DbSet<InboxThread> InboxThreads => Set<InboxThread>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<PushToken> PushTokens => Set<PushToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<ChannelAccount> ChannelAccounts => Set<ChannelAccount>();
    public DbSet<LeadProfile> LeadProfiles => Set<LeadProfile>();
    public DbSet<LeadEvent> LeadEvents => Set<LeadEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<User>().HasOne(x => x.ParentUser).WithMany().HasForeignKey(x => x.ParentUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<License>().HasIndex(x => x.Key).IsUnique();
        b.Entity<Activation>().HasOne(x => x.License).WithMany(x => x.Activations)
            .HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Activation>().HasIndex(x => new { x.LicenseId, x.Hwid });

        b.Entity<Contact>().HasOne(x => x.Group).WithMany(x => x.Contacts)
            .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Contact>().HasIndex(x => new { x.GroupId, x.Phone }).IsUnique();
        b.Entity<Contact>().HasIndex(x => x.Phone);

        b.Entity<CampaignRecipient>().HasOne(x => x.Campaign).WithMany(x => x.Recipients)
            .HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        // Duplicate-send guard: a phone appears at most once per campaign.
        b.Entity<CampaignRecipient>().HasIndex(x => new { x.CampaignId, x.Phone }).IsUnique();
        b.Entity<CampaignRecipient>().HasIndex(x => new { x.CampaignId, x.Status });

        b.Entity<CampaignCheckpoint>().HasIndex(x => x.CampaignId).IsUnique();
        b.Entity<AccountAntiBanSettings>().HasIndex(x => x.AccountId).IsUnique();
        b.Entity<WaSessionState>().HasIndex(x => new { x.AccountId, x.Key }).IsUnique();

        b.Entity<InboxMessage>().HasOne(x => x.Thread).WithMany(x => x.Messages)
            .HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<InboxThread>().HasIndex(x => new { x.AccountId, x.ContactPhone }).IsUnique();

        b.Entity<WebhookEvent>().HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
        b.Entity<PushToken>().HasIndex(x => x.Token).IsUnique();
        b.Entity<AppSetting>().HasIndex(x => x.Key).IsUnique();
        b.Entity<ChannelAccount>().HasIndex(x => new { x.OwnerId, x.Channel, x.ExternalAccountId });
        b.Entity<LeadProfile>().HasIndex(x => new { x.OwnerId, x.Status });
        b.Entity<LeadProfile>().HasIndex(x => new { x.OwnerId, x.IsConverted });
        b.Entity<LeadEvent>().HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(b);
    }
}
