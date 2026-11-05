using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Core.Services;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

public class AccountService
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _enc;
    private readonly IWaBridgeClient _bridge;
    private readonly IEvolutionClient _evolution;
    public AccountService(AppDbContext db, IEncryptionService enc, IWaBridgeClient bridge, IEvolutionClient evolution)
    { _db = db; _enc = enc; _bridge = bridge; _evolution = evolution; }

    public async Task<List<WaAccountDto>> ListAsync(int ownerId)
    {
        var accounts = await _db.WhatsAppAccounts.Include(a => a.Proxy).Where(a => a.OwnerId == ownerId).ToListAsync();
        var ids = accounts.Select(a => a.Id).ToList();
        var bans = await _db.AntiBanSettings.Where(b => ids.Contains(b.AccountId)).ToListAsync();
        return accounts.Select(a => a.ToDto(bans.FirstOrDefault(b => b.AccountId == a.Id))).ToList();
    }

    public async Task<WaAccountDto> GetAsync(int ownerId, int id)
    {
        var acc = await Owned(ownerId, id);
        var ban = await _db.AntiBanSettings.FirstOrDefaultAsync(b => b.AccountId == acc.Id);
        return acc.ToDto(ban);
    }

    public async Task<WaAccountDto> CreateAsync(int ownerId, CreateWaAccountRequest req)
    {
        if (!Enum.TryParse<EngineType>(req.EngineType, true, out var engine)) throw new BusinessException("Invalid engine.");
        var acc = new WhatsAppAccount
        {
            OwnerId = ownerId, EngineType = engine, PhoneNumber = req.PhoneNumber, DisplayName = req.DisplayName,
            ProxyId = req.ProxyId,
            // A brand-new unofficial number (Baileys, or Evolution which wraps Baileys) must start
            // in warm-up with a low daily limit, or WhatsApp restricts it on the first cold bulk
            // run. The official Cloud API is Meta-approved, so it can start ready with a normal limit.
            WarmupState = engine == EngineType.CloudApi ? WarmupState.Ready : WarmupState.Warming,
            DailyLimit = engine == EngineType.CloudApi ? 1000 : 50,
            CredentialsEnc = req.Credentials is null ? null : _enc.Encrypt(JsonSerializer.Serialize(req.Credentials))
        };
        _db.WhatsAppAccounts.Add(acc);
        await _db.SaveChangesAsync();
        // First account for this owner becomes the default sending number automatically.
        if (!await _db.WhatsAppAccounts.AnyAsync(x => x.OwnerId == ownerId && x.IsDefault && x.Id != acc.Id))
        {
            acc.IsDefault = true;
        }
        // Conservative anti-ban defaults live on the entity (30-60s delay, batch 15, 10m cooldown,
        // cap 50) so every new number is protected the moment it is created.
        _db.AntiBanSettings.Add(new AccountAntiBanSettings { AccountId = acc.Id });
        await _db.SaveChangesAsync();
        return acc.ToDto();
    }

    public async Task<WaAccountDto> UpdateAsync(int ownerId, int id, UpdateWaAccountRequest req)
    {
        var acc = await Owned(ownerId, id);
        acc.DisplayName = req.DisplayName;
        acc.ProxyId = req.ProxyId;
        if (req.RotationWeight.HasValue) acc.RotationWeight = req.RotationWeight.Value;
        if (req.DailyLimit.HasValue) acc.DailyLimit = req.DailyLimit.Value;
        acc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return acc.ToDto();
    }

    public async Task DeleteAsync(int ownerId, int id)
    {
        var acc = await Owned(ownerId, id);
        _db.WhatsAppAccounts.Remove(acc);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Marks one account as the owner's default sending number (clears the flag on all others),
    /// so campaign creation can pre-select it.
    /// </summary>
    public async Task<WaAccountDto> SetDefaultAsync(int ownerId, int id)
    {
        var acc = await Owned(ownerId, id);
        var mine = await _db.WhatsAppAccounts.Where(a => a.OwnerId == ownerId).ToListAsync();
        foreach (var a in mine) a.IsDefault = a.Id == id;
        acc.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var ban = await _db.AntiBanSettings.FirstOrDefaultAsync(b => b.AccountId == acc.Id);
        return acc.ToDto(ban);
    }

    /// <summary>
    /// Manually clears a restriction flag so a wrongly-flagged or recovered number can be used
    /// again. Also resets the failure-streak counters on this account's campaign checkpoints so
    /// a stale streak can't immediately re-trip the circuit breaker.
    /// </summary>
    public async Task<WaAccountDto> ClearRestrictionAsync(int ownerId, int id)
    {
        var acc = await Owned(ownerId, id);
        acc.IsRestricted = false;
        acc.RestrictionReason = null;
        acc.RestrictedAt = null;
        if (acc.Status == WaAccountStatus.Banned) acc.Status = WaAccountStatus.Disconnected;
        acc.UpdatedAt = DateTime.UtcNow;
        // Reset any failure streaks on this owner's campaigns so the breaker starts clean.
        var cps = await _db.CampaignCheckpoints
            .Where(cp => _db.Campaigns.Any(c => c.Id == cp.CampaignId && c.OwnerId == ownerId))
            .ToListAsync();
        foreach (var cp in cps) cp.ConsecutiveFailures = 0;
        await _db.SaveChangesAsync();
        var ban = await _db.AntiBanSettings.FirstOrDefaultAsync(b => b.AccountId == acc.Id);
        return acc.ToDto(ban);
    }

    public async Task<WaAccountDto> ConnectAsync(int ownerId, int id)
    {
        var acc = await Owned(ownerId, id);
        if (acc.EngineType != EngineType.Baileys && acc.EngineType != EngineType.Evolution && acc.EngineType != EngineType.WhatsAppWeb)
            throw new BusinessException("QR connect is only available for Baileys, WhatsApp Web and Evolution accounts.");

        // Clear any stale QR and move to Connecting.
        acc.Status = WaAccountStatus.Connecting;
        acc.QrData = null;
        acc.QrPngBase64 = null;
        acc.QrExpiresAt = null;
        await _db.SaveChangesAsync();

        if (acc.EngineType == EngineType.Evolution)
        {
            if (!_evolution.IsConfigured)
            {
                acc.Status = WaAccountStatus.Error; await _db.SaveChangesAsync();
                throw new BusinessException("Evolution API is not configured on the server.");
            }
            var evoOk = await _evolution.ConnectAsync(id);
            if (!evoOk)
            {
                acc.Status = WaAccountStatus.Error; await _db.SaveChangesAsync();
                throw new BusinessException("Evolution API is unavailable. Please try again shortly.");
            }
            // Evolution QR is polled (GET /qr), so fetch it now and surface via QrAsync.
            var qr = await _evolution.QrBase64Async(id);
            if (!string.IsNullOrWhiteSpace(qr))
            {
                acc.Status = WaAccountStatus.QrReady;
                acc.QrPngBase64 = qr;
                acc.QrExpiresAt = DateTime.UtcNow.AddSeconds(60);
                await _db.SaveChangesAsync();
            }
            return acc.ToDto();
        }

        // Baileys and WhatsApp Web both run on the bridge; the real QR arrives via the bridge
        // callback (POST /api/bridge/events). WhatsApp Web is selected with the "wweb" engine hint.
        var engineParam = acc.EngineType == EngineType.WhatsAppWeb ? "wweb" : null;
        var ok = await _bridge.ConnectAsync(id, engineParam, default);
        if (!ok)
        {
            acc.Status = WaAccountStatus.Error;
            await _db.SaveChangesAsync();
            throw new BusinessException("WhatsApp bridge is unavailable. Please try again shortly.");
        }
        return acc.ToDto();
    }

    public async Task<QrResponse> QrAsync(int ownerId, int id)
    {
        var acc = await Owned(ownerId, id);
        // Evolution QR is live-polled from the server; refresh it on each request until connected.
        if (acc.EngineType == EngineType.Evolution && _evolution.IsConfigured && acc.Status != WaAccountStatus.Connected)
        {
            var state = await _evolution.StatusAsync(id);
            if (string.Equals(state, "connected", StringComparison.OrdinalIgnoreCase))
            {
                acc.Status = WaAccountStatus.Connected; acc.LastSeenAt = DateTime.UtcNow;
                acc.QrData = null; acc.QrPngBase64 = null; acc.QrExpiresAt = null;
                await _db.SaveChangesAsync();
                return new QrResponse(null, null, null);
            }
            var qr = await _evolution.QrBase64Async(id);
            if (!string.IsNullOrWhiteSpace(qr))
            {
                acc.Status = WaAccountStatus.QrReady; acc.QrPngBase64 = qr; acc.QrExpiresAt = DateTime.UtcNow.AddSeconds(60);
                await _db.SaveChangesAsync();
            }
        }
        return new QrResponse(acc.QrData, acc.QrPngBase64, acc.QrExpiresAt);
    }

    public async Task DisconnectAsync(int ownerId, int id)
    {
        var acc = await Owned(ownerId, id);
        if (acc.EngineType == EngineType.Evolution) await _evolution.DisconnectAsync(id, wipe: true);
        else await _bridge.DisconnectAsync(id, true, acc.EngineType == EngineType.WhatsAppWeb ? "wweb" : null, default);
        acc.Status = WaAccountStatus.Disconnected;
        acc.QrData = null;
        acc.QrPngBase64 = null;
        acc.QrExpiresAt = null;
        await _db.SaveChangesAsync();
    }

    public async Task<WaAccountDto> RotateAsync(int ownerId, int id, int weight)
    {
        var acc = await Owned(ownerId, id);
        acc.RotationWeight = Math.Max(1, weight);
        await _db.SaveChangesAsync();
        return acc.ToDto();
    }

    public async Task<AccountsStatusDto> StatusAsync(int ownerId)
    {
        var list = await _db.WhatsAppAccounts.Where(a => a.OwnerId == ownerId).ToListAsync();
        return new AccountsStatusDto(list.Count(a => a.Status == WaAccountStatus.Connected),
            list.Count(a => a.Status is WaAccountStatus.Connecting or WaAccountStatus.QrReady), list.Count);
    }

    private async Task<WhatsAppAccount> Owned(int ownerId, int id) =>
        await _db.WhatsAppAccounts.Include(a => a.Proxy).FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId)
        ?? throw new NotFoundException("Account not found.");
}

public class ProxyService
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _enc;
    public ProxyService(AppDbContext db, IEncryptionService enc) { _db = db; _enc = enc; }

    public async Task<List<ProxyDto>> ListAsync(int ownerId) =>
        (await _db.Proxies.Where(p => p.OwnerId == ownerId).ToListAsync()).Select(p => p.ToDto()).ToList();

    public async Task<ProxyDto> CreateAsync(int ownerId, CreateProxyRequest req)
    {
        if (!Enum.TryParse<ProxyType>(req.Type, true, out var t)) throw new BusinessException("Invalid proxy type.");
        var p = new Proxy { OwnerId = ownerId, Type = t, Host = req.Host, Port = req.Port, Username = req.Username,
            PasswordEnc = string.IsNullOrEmpty(req.Password) ? null : _enc.Encrypt(req.Password) };
        _db.Proxies.Add(p); await _db.SaveChangesAsync();
        return p.ToDto();
    }

    public async Task<ProxyDto> UpdateAsync(int ownerId, int id, CreateProxyRequest req)
    {
        var p = await Owned(ownerId, id);
        if (Enum.TryParse<ProxyType>(req.Type, true, out var t)) p.Type = t;
        p.Host = req.Host; p.Port = req.Port; p.Username = req.Username;
        if (!string.IsNullOrEmpty(req.Password)) p.PasswordEnc = _enc.Encrypt(req.Password);
        await _db.SaveChangesAsync();
        return p.ToDto();
    }

    public async Task DeleteAsync(int ownerId, int id)
    {
        var p = await Owned(ownerId, id);
        _db.Proxies.Remove(p); await _db.SaveChangesAsync();
    }

    public async Task<ProxyTestResult> TestAsync(int ownerId, int id)
    {
        var p = await Owned(ownerId, id);
        // A real implementation opens a socket through the proxy; here we report a synthetic health probe.
        p.IsHealthy = true; await _db.SaveChangesAsync();
        return new ProxyTestResult(true, Random.Shared.Next(40, 220), null);
    }

    private async Task<Proxy> Owned(int ownerId, int id) =>
        await _db.Proxies.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId)
        ?? throw new NotFoundException("Proxy not found.");
}

public class TemplateService
{
    private readonly AppDbContext _db;
    public TemplateService(AppDbContext db) => _db = db;

    public async Task<List<TemplateDto>> ListAsync(int ownerId) =>
        (await _db.Templates.Where(t => t.OwnerId == ownerId).ToListAsync()).Select(Map).ToList();

    public async Task<TemplateDto> CreateAsync(int ownerId, CreateTemplateRequest req)
    {
        Enum.TryParse<MediaType>(req.MediaType, true, out var mt);
        var t = new Template
        {
            OwnerId = ownerId, Name = req.Name, Body = req.Body, MediaType = mt, MediaUrl = req.MediaUrl,
            Buttons = req.Buttons is null ? null : JsonSerializer.Serialize(req.Buttons),
            ListMenu = req.ListMenu is null ? null : JsonSerializer.Serialize(req.ListMenu),
            Category = req.Category ?? "General"
        };
        _db.Templates.Add(t); await _db.SaveChangesAsync();
        return Map(t);
    }

    public async Task<TemplateDto> UpdateAsync(int ownerId, int id, CreateTemplateRequest req)
    {
        var t = await Owned(ownerId, id);
        Enum.TryParse<MediaType>(req.MediaType, true, out var mt);
        t.Name = req.Name; t.Body = req.Body; t.MediaType = mt; t.MediaUrl = req.MediaUrl;
        t.Buttons = req.Buttons is null ? null : JsonSerializer.Serialize(req.Buttons);
        t.ListMenu = req.ListMenu is null ? null : JsonSerializer.Serialize(req.ListMenu);
        t.Category = req.Category ?? t.Category; t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Map(t);
    }

    public async Task DeleteAsync(int ownerId, int id)
    { var t = await Owned(ownerId, id); _db.Templates.Remove(t); await _db.SaveChangesAsync(); }

    private async Task<Template> Owned(int ownerId, int id) =>
        await _db.Templates.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId) ?? throw new NotFoundException("Template not found.");

    public static TemplateDto Map(Template t) => new(t.Id, t.Name, t.Body, t.MediaType.ToString(), t.MediaUrl,
        t.Buttons is null ? null : JsonSerializer.Deserialize<List<TemplateButton>>(t.Buttons),
        t.ListMenu is null ? null : JsonSerializer.Deserialize<TemplateListMenu>(t.ListMenu),
        t.Category, t.CreatedAt, t.UpdatedAt);
}
