using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinMaster.Core.Interfaces;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddWinMasterInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        var provider = (cfg["Database:Provider"] ?? "Postgres").Trim();
        var raw = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? cfg.GetConnectionString("DefaultConnection")
            ?? "Data Source=winmaster.db";

        services.AddDbContext<AppDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(raw.StartsWith("Data Source") ? raw : "Data Source=winmaster.db");
            else
                options.UseNpgsql(NormalizeNpgsql(raw));
        });

        services.AddDataProtection();
        services.AddHttpClient("bridge");
        services.AddHttpClient("meta", c => c.BaseAddress = new Uri("https://graph.facebook.com/v20.0/"))
            .AddStandardResilienceHandler();

        services.AddSingleton<ILicenseSigner>(sp =>
            new LicenseSigner(cfg["License:SigningSecret"] ?? "winmaster-license-signing-secret-change-me"));
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<IWhatsAppChannel, MetaCloudChannel>();
        services.AddScoped<IWhatsAppChannel, BaileysBridgeChannel>();
        services.AddScoped<IWhatsAppChannelFactory, WhatsAppChannelFactory>();
        services.AddSingleton<IPushService, NoOpPushService>();

        services.AddSingleton<TokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<LicenseService>();
        services.AddScoped<AccountService>();
        services.AddScoped<ProxyService>();
        services.AddScoped<ContactService>();
        services.AddScoped<TemplateService>();
        services.AddScoped<CampaignService>();
        services.AddScoped<AntiBanService>();
        services.AddScoped<RuleService>();
        services.AddScoped<AiService>();
        services.AddScoped<InboxService>();
        services.AddScoped<ReportService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<BridgeService>();
        services.AddScoped<PushTokenService>();

        services.AddHostedService<CampaignWorker>();
        services.AddHostedService<SchedulerService>();

        return services;
    }

    public static string NormalizeNpgsql(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return value;
        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var db = uri.AbsolutePath.Trim('/');
        var sslmode = ParseQuery(uri.Query).GetValueOrDefault("sslmode");
        var cs = $"Host={uri.Host};Port={(uri.IsDefaultPort ? 5432 : uri.Port)};Database={db};" +
                 $"Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? "")}";
        if (!string.IsNullOrEmpty(sslmode)) cs += $";SSL Mode={(sslmode == "disable" ? "Disable" : "Require")};Trust Server Certificate=true";
        return cs;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            dict[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        return dict;
    }
}
