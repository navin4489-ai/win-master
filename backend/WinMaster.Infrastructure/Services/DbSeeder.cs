using WinMaster.Core;
using WinMaster.Core.Entities;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

/// <summary>Idempotent demo seeding so every screen has data on first run.</summary>
public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (db.Users.Any()) return;

        foreach (var r in Enum.GetValues<UserRole>())
            db.Roles.Add(new Role { Name = r });
        db.SaveChanges();

        var admin = new User { Email = "admin@winmaster.io", DisplayName = "Super Admin", Role = UserRole.SuperAdmin, PasswordHash = PasswordHasher.Hash("Win@12345"), ResellerCode = "WM-ROOT" };
        db.Users.Add(admin); db.SaveChanges();
        var reseller = new User { Email = "reseller@winmaster.io", DisplayName = "Demo Reseller", Role = UserRole.Reseller, ParentUserId = admin.Id, PasswordHash = PasswordHasher.Hash("Win@12345"), ResellerCode = "WM-RS01" };
        db.Users.Add(reseller); db.SaveChanges();
        var user = new User { Email = "user@winmaster.io", DisplayName = "Demo Marketer", Role = UserRole.User, ParentUserId = reseller.Id, PasswordHash = PasswordHasher.Hash("Win@12345") };
        db.Users.Add(user); db.SaveChanges();

        // Licenses (5)
        db.Licenses.AddRange(
            new License { Key = "WM-DEMO-AAAA-BBBB-CCCC", LicenseType = LicenseType.Admin, IssuedById = admin.Id, Features = "campaigns,inbox,ai,licensing", MaxActivations = 3, ExpiresAt = DateTime.UtcNow.AddYears(1) },
            new License { Key = "WM-DEMO-DDDD-EEEE-FFFF", LicenseType = LicenseType.Reseller, IssuedById = admin.Id, UserId = reseller.Id, Features = "campaigns,inbox,licensing", MaxActivations = 5, ExpiresAt = DateTime.UtcNow.AddYears(1) },
            new License { Key = "WM-DEMO-GGGG-HHHH-JJJJ", LicenseType = LicenseType.User, IssuedById = reseller.Id, UserId = user.Id, Features = "campaigns,inbox", MaxActivations = 1, ExpiresAt = DateTime.UtcNow.AddMonths(6) },
            new License { Key = "WM-DEMO-KKKK-MMMM-NNNN", LicenseType = LicenseType.User, IssuedById = reseller.Id, Features = "campaigns", MaxActivations = 1, ExpiresAt = DateTime.UtcNow.AddDays(10) },
            new License { Key = "WM-DEMO-PPPP-QQQQ-RRRR", LicenseType = LicenseType.User, IssuedById = reseller.Id, Features = "campaigns", MaxActivations = 1, Status = LicenseStatus.Revoked });
        db.SaveChanges();

        var proxy = new Proxy { OwnerId = user.Id, Type = ProxyType.SOCKS5, Host = "127.0.0.1", Port = 9050, Username = "proxyuser" };
        db.Proxies.Add(proxy); db.SaveChanges();

        var acc1 = new WhatsAppAccount { OwnerId = user.Id, EngineType = EngineType.Baileys, DisplayName = "Marketing Line", PhoneNumber = "+15550100001", Status = WaAccountStatus.Connected, WarmupState = WarmupState.Ready, ProxyId = proxy.Id, DailyLimit = 1000, SentToday = 120 };
        var acc2 = new WhatsAppAccount { OwnerId = user.Id, EngineType = EngineType.CloudApi, DisplayName = "Official Business", PhoneNumber = "+15550100002", Status = WaAccountStatus.Connected, WarmupState = WarmupState.Ready, DailyLimit = 5000, SentToday = 340 };
        db.WhatsAppAccounts.AddRange(acc1, acc2); db.SaveChanges();

        db.AntiBanSettings.AddRange(
            new AccountAntiBanSettings { AccountId = acc1.Id, MinDelaySec = 6, MaxDelaySec = 14, BatchSize = 40, CooldownMinutes = 5, DailyCap = 800 },
            new AccountAntiBanSettings { AccountId = acc2.Id, MinDelaySec = 3, MaxDelaySec = 8, BatchSize = 100, CooldownMinutes = 3, DailyCap = 4000 });

        db.AiSettings.Add(new AiSettings { OwnerId = user.Id, Provider = AiProvider.Local, Model = "local-generator", IsActive = true, SystemPrompt = "You are a friendly sales assistant for Win Master." });

        // Templates (2)
        var tpl1 = new Template { OwnerId = user.Id, Name = "Welcome Offer", Body = "{Hi|Hello|Hey} {{Name}}! Your invoice {{InvoiceNo}} is ready. Reply to continue.", Category = "Promo" };
        var tpl2 = new Template { OwnerId = user.Id, Name = "Reminder", Body = "{{Name}}, a quick reminder about your appointment.", Category = "Reminder" };
        db.Templates.AddRange(tpl1, tpl2); db.SaveChanges();

        // Groups (50 + 5000)
        var g1 = new ContactGroup { OwnerId = user.Id, Name = "VIP Customers", Source = ContactSource.Import, Tags = "vip,priority" };
        var g2 = new ContactGroup { OwnerId = user.Id, Name = "Newsletter 5k", Source = ContactSource.Import, Tags = "newsletter" };
        db.ContactGroups.AddRange(g1, g2); db.SaveChanges();

        var c1 = new List<Contact>();
        for (int i = 1; i <= 50; i++)
            c1.Add(new Contact { GroupId = g1.Id, Phone = $"+1555020{i:0000}", Name = $"VIP {i}", WaRegistered = true, CustomFields = $"{{\"InvoiceNo\":\"INV-{1000 + i}\"}}", Tags = "vip" });
        db.Contacts.AddRange(c1);
        var c2 = new List<Contact>();
        for (int i = 1; i <= 5000; i++)
            c2.Add(new Contact { GroupId = g2.Id, Phone = $"+1555{i:0000000}", Name = $"Sub {i}", WaRegistered = i % 7 != 0, CustomFields = "{}", Tags = "newsletter" });
        db.Contacts.AddRange(c2);
        g1.TotalContacts = 50; g1.ValidContacts = 50;
        g2.TotalContacts = 5000; g2.ValidContacts = c2.Count(x => x.WaRegistered == true); g2.InvalidContacts = 5000 - g2.ValidContacts;
        db.SaveChanges();

        db.Tags.AddRange(
            new Tag { OwnerId = user.Id, Name = "vip", Color = "#e11d48" },
            new Tag { OwnerId = user.Id, Name = "newsletter", Color = "#2563eb" },
            new Tag { OwnerId = user.Id, Name = "lead", Color = "#16a34a" });

        // Campaigns (Completed / Running / Scheduled)
        var done = new Campaign { OwnerId = user.Id, Name = "October Promo", TemplateId = tpl1.Id, GroupId = g1.Id, EngineType = EngineType.Baileys, AccountId = acc1.Id, Status = CampaignStatus.Completed, TotalRecipients = 50, ProcessedCount = 50, FailedCount = 2, StartedAt = DateTime.UtcNow.AddDays(-2), CompletedAt = DateTime.UtcNow.AddDays(-2).AddMinutes(30) };
        var running = new Campaign { OwnerId = user.Id, Name = "Flash Sale", TemplateId = tpl1.Id, GroupId = g2.Id, EngineType = EngineType.CloudApi, AccountId = acc2.Id, Status = CampaignStatus.Running, TotalRecipients = 5000, ProcessedCount = 1200, FailedCount = 15, StartedAt = DateTime.UtcNow.AddMinutes(-20) };
        var scheduled = new Campaign { OwnerId = user.Id, Name = "Weekend Reminder", TemplateId = tpl2.Id, GroupId = g1.Id, EngineType = EngineType.Baileys, Status = CampaignStatus.Scheduled, ScheduledAt = DateTime.UtcNow.AddHours(6), TotalRecipients = 50 };
        db.Campaigns.AddRange(done, running, scheduled); db.SaveChanges();

        db.AutoReplyRules.AddRange(
            new AutoReplyRule { OwnerId = user.Id, AccountId = acc1.Id, MatchType = WinMaster.Core.MatchType.Exact, Pattern = "hi", Priority = 1, ResponseText = "Hello! How can we help you today?", IsActive = true },
            new AutoReplyRule { OwnerId = user.Id, AccountId = acc1.Id, MatchType = WinMaster.Core.MatchType.Wildcard, Pattern = "*price*", Priority = 2, ResponseText = "Our pricing starts at $49. Want details?", IsActive = true },
            new AutoReplyRule { OwnerId = user.Id, AccountId = acc2.Id, MatchType = WinMaster.Core.MatchType.Regex, Pattern = "^(stop|unsubscribe)$", Priority = 0, ResponseText = "You have been unsubscribed.", IsActive = true });

        // Inbox threads + messages
        var th1 = new InboxThread { OwnerId = user.Id, AccountId = acc1.Id, ContactPhone = "+15550200010", ContactName = "VIP 10", UnreadCount = 2, Preview = "Is this still available?", LastMessageAt = DateTime.UtcNow.AddMinutes(-5) };
        var th2 = new InboxThread { OwnerId = user.Id, AccountId = acc2.Id, ContactPhone = "+15550000042", ContactName = "Sub 42", UnreadCount = 0, Preview = "Thanks!", LastMessageAt = DateTime.UtcNow.AddHours(-1) };
        db.InboxThreads.AddRange(th1, th2); db.SaveChanges();
        db.InboxMessages.AddRange(
            new InboxMessage { ThreadId = th1.Id, Direction = MessageDirection.In, Body = "Hi, I saw your offer", SentAt = DateTime.UtcNow.AddMinutes(-7) },
            new InboxMessage { ThreadId = th1.Id, Direction = MessageDirection.Out, Body = "Hello! Yes it is available.", Status = MessageStatus.Read, SentAt = DateTime.UtcNow.AddMinutes(-6) },
            new InboxMessage { ThreadId = th1.Id, Direction = MessageDirection.In, Body = "Is this still available?", SentAt = DateTime.UtcNow.AddMinutes(-5) },
            new InboxMessage { ThreadId = th2.Id, Direction = MessageDirection.Out, Body = "Your order shipped.", Status = MessageStatus.Delivered, SentAt = DateTime.UtcNow.AddHours(-1) });

        db.AppSettings.Add(new AppSetting { Key = "branding", ValueJson = "{\"appName\":\"Win Master\",\"primaryColor\":\"#25D366\"}" });
        db.SaveChanges();
    }
}
