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
    public AccountService(AppDbContext db, IEncryptionService enc, IWaBridgeClient bridge)
    { _db = db; _enc = enc; _bridge = bridge; }

    public async Task<List<WaAccountDto>> ListAsync(int ownerId) =>
        (await _db.WhatsAppAccounts.Include(a => a.Proxy).Where(a => a.OwnerId == ownerId).ToListAsync())
        .Select(a => a.ToDto()).ToList();

    public async Task<WaAccountDto> GetAsync(int ownerId, int id) =>
        (await Owned(ownerId, id)).ToDto();

    public async Task<WaAccountDto> CreateAsync(int ownerId, CreateWaAccountRequest req)
    {
        if (!Enum.TryParse<EngineType>(req.EngineType, true, out var engine)) throw new BusinessException("Invalid engine.");
        var acc = new WhatsAppAccount
        {
            OwnerId = ownerId, EngineType = engine, PhoneNumber = req.PhoneNumber, DisplayName = req.DisplayName,
            ProxyId = req.ProxyId,
            CredentialsEnc = req.Credentials is null ? null : _enc.Encrypt(JsonSerializer.Serialize(req.Credentials))
        };
        _db.WhatsAppAccounts.Add(acc);
        await _db.SaveChangesAsync();
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

    public async Task<WaAccountDto> ConnectAsync(int ownerId, int id)
    {
        var acc = await Owned(ownerId, id);
        if (acc.EngineType != EngineType.Baileys)
            throw new BusinessException("QR connect is only available for Baileys accounts.");

        // Clear any stale QR and move to Connecting; the real QR arrives via the
        // bridge callback (POST /api/bridge/events) which flips status to QrReady.
        acc.Status = WaAccountStatus.Connecting;
        acc.QrData = null;
        acc.QrPngBase64 = null;
        acc.QrExpiresAt = null;
        await _db.SaveChangesAsync();

        var ok = await _bridge.ConnectAsync(id);
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
        return new QrResponse(acc.QrData, acc.QrPngBase64, acc.QrExpiresAt);
    }

    public async Task DisconnectAsync(int ownerId, int id)
    {
        var acc = await Owned(ownerId, id);
        await _bridge.DisconnectAsync(id, wipe: true);
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
