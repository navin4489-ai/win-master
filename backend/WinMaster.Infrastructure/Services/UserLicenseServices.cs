using Microsoft.EntityFrameworkCore;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Core.Interfaces;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

public class UserService
{
    private readonly AppDbContext _db;
    public UserService(AppDbContext db) => _db = db;

    public async Task<PagedResult<UserDto>> ListAsync(UserRole actorRole, int actorId, string? role, int? parentId, PageQuery q)
    {
        var query = _db.Users.AsQueryable();
        if (actorRole != UserRole.SuperAdmin)
        {
            var subtree = await SubtreeIdsAsync(actorId);
            query = query.Where(u => subtree.Contains(u.Id) && u.Id != actorId);
        }
        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, true, out var r))
            query = query.Where(u => u.Role == r);
        if (parentId.HasValue) query = query.Where(u => u.ParentUserId == parentId);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(u => u.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
        return new PagedResult<UserDto>(items.Select(u => u.ToDto()).ToList(), q.Page, q.PageSize, total);
    }

    public async Task<UserDto> CreateAsync(UserRole actorRole, int actorId, CreateUserRequest req)
    {
        if (!Enum.TryParse<UserRole>(req.Role, true, out var newRole))
            throw new BusinessException("Invalid role.");
        if (!Hierarchy.CanManage(actorRole, newRole))
            throw new ForbiddenException("You cannot create a user of that role.");
        if (await _db.Users.AnyAsync(u => u.Email == req.Email.ToLower()))
            throw new ConflictException("Email already exists.");

        var user = new User
        {
            Email = req.Email.Trim().ToLower(),
            DisplayName = req.DisplayName,
            Role = newRole,
            PasswordHash = PasswordHasher.Hash(req.Password),
            ParentUserId = req.ParentUserId ?? actorId,
            ResellerCode = newRole >= UserRole.Reseller ? $"WM-{Random.Shared.Next(1000, 9999)}" : null
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user.ToDto();
    }

    public async Task<UserDto> UpdateAsync(UserRole actorRole, int actorId, int id, UpdateUserRequest req)
    {
        var user = await GetVisibleAsync(actorRole, actorId, id);
        user.DisplayName = req.DisplayName;
        if (req.Role is not null && Enum.TryParse<UserRole>(req.Role, true, out var r))
        {
            if (!Hierarchy.CanManage(actorRole, r)) throw new ForbiddenException("Role not allowed.");
            user.Role = r;
        }
        if (req.IsActive.HasValue) user.IsActive = req.IsActive.Value;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return user.ToDto();
    }

    public async Task DeleteAsync(UserRole actorRole, int actorId, int id)
    {
        var user = await GetVisibleAsync(actorRole, actorId, id);
        user.IsActive = false;
        await _db.SaveChangesAsync();
    }

    public async Task<List<UserTreeNode>> TreeAsync(UserRole actorRole, int actorId)
    {
        var all = actorRole == UserRole.SuperAdmin
            ? await _db.Users.ToListAsync()
            : await _db.Users.Where(u => true).ToListAsync();
        var rootId = actorRole == UserRole.SuperAdmin ? (int?)null : actorId;
        List<UserTreeNode> Build(int? parent) => all.Where(u => u.ParentUserId == parent)
            .Select(u => new UserTreeNode(u.Id, u.Email, u.DisplayName, u.Role.ToString(), Build(u.Id))).ToList();
        if (rootId is null) return Build(null);
        var self = all.First(u => u.Id == actorId);
        return new List<UserTreeNode> { new(self.Id, self.Email, self.DisplayName, self.Role.ToString(), Build(self.Id)) };
    }

    private async Task<User> GetVisibleAsync(UserRole actorRole, int actorId, int id)
    {
        var user = await _db.Users.FindAsync(id) ?? throw new NotFoundException("User not found.");
        if (actorRole != UserRole.SuperAdmin)
        {
            var subtree = await SubtreeIdsAsync(actorId);
            if (!subtree.Contains(id)) throw new NotFoundException("User not found.");
        }
        return user;
    }

    public async Task<HashSet<int>> SubtreeIdsAsync(int rootId)
    {
        var all = await _db.Users.Select(u => new { u.Id, u.ParentUserId }).ToListAsync();
        var result = new HashSet<int> { rootId };
        bool added = true;
        while (added)
        {
            added = false;
            foreach (var u in all)
                if (u.ParentUserId.HasValue && result.Contains(u.ParentUserId.Value) && result.Add(u.Id))
                    added = true;
        }
        return result;
    }
}

public class LicenseService
{
    private readonly AppDbContext _db;
    private readonly ILicenseSigner _signer;
    private readonly UserService _users;
    private const int OfflineGraceDays = 7;

    public LicenseService(AppDbContext db, ILicenseSigner signer, UserService users)
    { _db = db; _signer = signer; _users = users; }

    public async Task<PagedResult<LicenseDto>> ListAsync(UserRole actorRole, int actorId, string? status, string? type, string? q, PageQuery page)
    {
        var query = _db.Licenses.Include(l => l.Activations).Include(l => l.User).AsQueryable();
        if (actorRole != UserRole.SuperAdmin)
        {
            var subtree = await _users.SubtreeIdsAsync(actorId);
            query = query.Where(l => l.IssuedById == actorId || (l.UserId != null && subtree.Contains(l.UserId.Value)) || subtree.Contains(l.IssuedById));
        }
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<LicenseType>(type, true, out var t)) query = query.Where(l => l.LicenseType == t);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(l => l.Key.Contains(q));
        var all = await query.OrderByDescending(l => l.Id).ToListAsync();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<LicenseStatus>(status, true, out var st))
            all = all.Where(l => Mapping.EffectiveStatus(l) == st).ToList();
        var total = all.Count;
        var items = all.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).Select(l => l.ToDto()).ToList();
        return new PagedResult<LicenseDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<LicenseDto> CreateAsync(UserRole actorRole, int actorId, CreateLicenseRequest req)
    {
        if (!Hierarchy.AtLeast(actorRole, UserRole.Reseller))
            throw new ForbiddenException("Only Reseller and above can issue licenses.");
        if (!Enum.TryParse<LicenseType>(req.LicenseType, true, out var type))
            throw new BusinessException("Invalid license type.");
        // A reseller can only mint User licenses; Admin can mint Reseller/User; SuperAdmin anything.
        var maxIssuable = actorRole switch
        {
            UserRole.Reseller => LicenseType.User,
            UserRole.Admin => LicenseType.Reseller,
            _ => LicenseType.Admin
        };
        if ((int)type > (int)maxIssuable) throw new ForbiddenException("License type exceeds your tier.");

        var license = new License
        {
            Key = _signer.GenerateKey(type),
            LicenseType = type,
            IssuedById = actorId,
            UserId = req.UserId,
            Features = string.Join(',', req.Features ?? Array.Empty<string>()),
            StartsAt = req.StartsAt == default ? DateTime.UtcNow : req.StartsAt,
            ExpiresAt = req.ExpiresAt,
            MaxActivations = Math.Max(1, req.MaxActivations),
            Status = LicenseStatus.Active
        };
        _db.Licenses.Add(license);
        await _db.SaveChangesAsync();
        return (await _db.Licenses.Include(l => l.Activations).Include(l => l.User).FirstAsync(l => l.Id == license.Id)).ToDto();
    }

    public async Task<LicenseDto> RevokeAsync(UserRole actorRole, int actorId, int id)
    {
        var license = await _db.Licenses.Include(l => l.Activations).Include(l => l.User).FirstOrDefaultAsync(l => l.Id == id)
            ?? throw new NotFoundException("License not found.");
        if (actorRole != UserRole.SuperAdmin && license.IssuedById != actorId)
        {
            var subtree = await _users.SubtreeIdsAsync(actorId);
            if (!subtree.Contains(license.IssuedById)) throw new ForbiddenException("Not your license.");
        }
        license.Status = LicenseStatus.Revoked;
        foreach (var a in license.Activations) a.RevokedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return license.ToDto();
    }

    public async Task<List<ActivationDto>> ActivationsAsync(int licenseId) =>
        (await _db.Activations.Where(a => a.LicenseId == licenseId).ToListAsync()).Select(a => a.ToDto()).ToList();

    public async Task RevokeDeviceAsync(int activationId)
    {
        var a = await _db.Activations.FindAsync(activationId) ?? throw new NotFoundException("Activation not found.");
        a.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ---- public endpoints (HWID / offline) ----
    public async Task<ActivateResponse> ActivateAsync(ActivateRequest req)
    {
        var license = await _db.Licenses.Include(l => l.Activations).Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Key == req.Key) ?? throw new NotFoundException("License key not found.");
        if (Mapping.EffectiveStatus(license) != LicenseStatus.Active)
            throw new BusinessException("License is not active.");

        var existing = license.Activations.FirstOrDefault(a => a.Hwid == req.Hwid && a.RevokedAt == null);
        if (existing is null)
        {
            var activeCount = license.Activations.Count(a => a.RevokedAt == null);
            if (activeCount >= license.MaxActivations)
                throw new BusinessException("Maximum activations reached.");
            existing = new Activation { LicenseId = license.Id, Hwid = req.Hwid, DeviceName = req.DeviceName, LastVerifiedAt = DateTime.UtcNow };
            _db.Activations.Add(existing);
        }
        else existing.LastVerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = _signer.Sign(license.Key, req.Hwid, license.ExpiresAt);
        return new ActivateResponse(license.ToDto(), token, 24, OfflineGraceDays);
    }

    public VerifyResponse VerifyOffline(VerifyRequest req, License? license)
    {
        // Offline-capable: the signed token proves validity even when the server view is stale.
        if (license is not null && Mapping.EffectiveStatus(license) == LicenseStatus.Revoked)
            return new VerifyResponse(false, "Revoked", license.ExpiresAt);
        var expires = license?.ExpiresAt;
        var signatureOk = _signer.Verify(req.Key, req.Hwid, expires, req.Token);
        if (!signatureOk) return new VerifyResponse(false, "Invalid signature", expires);
        if (expires.HasValue && expires.Value.AddDays(OfflineGraceDays) < DateTime.UtcNow)
            return new VerifyResponse(false, "Expired", expires);
        return new VerifyResponse(true, null, expires);
    }

    public async Task<VerifyResponse> VerifyAsync(VerifyRequest req)
    {
        var license = await _db.Licenses.FirstOrDefaultAsync(l => l.Key == req.Key);
        var result = VerifyOffline(req, license);
        if (result.Valid && license is not null)
        {
            var act = await _db.Activations.FirstOrDefaultAsync(a => a.LicenseId == license.Id && a.Hwid == req.Hwid && a.RevokedAt == null);
            if (act is null) return new VerifyResponse(false, "Device not activated", license.ExpiresAt);
            act.LastVerifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return result;
    }

    public async Task DeactivateAsync(DeactivateRequest req)
    {
        var license = await _db.Licenses.Include(l => l.Activations).FirstOrDefaultAsync(l => l.Key == req.Key);
        var act = license?.Activations.FirstOrDefault(a => a.Hwid == req.Hwid && a.RevokedAt == null);
        if (act is not null) { act.RevokedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
    }
}
