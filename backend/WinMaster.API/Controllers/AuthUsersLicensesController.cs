using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Infrastructure.Services;

namespace WinMaster.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req) => Ok(await _auth.LoginAsync(req));

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest req) => Ok(await _auth.RefreshAsync(req));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest req) { await _auth.LogoutAsync(new RefreshRequest(req.RefreshToken)); return NoContent(); }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me() => Ok(await _auth.MeAsync(UserId));
}

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/users")]
public class UsersController : ApiControllerBase
{
    private readonly UserService _users;
    private readonly SettingsService _settings;
    public UsersController(UserService users, SettingsService settings) { _users = users; _settings = settings; }

    [HttpGet]
    public async Task<ActionResult<PagedResult<UserDto>>> List(string? role, int? parentId, int page = 1, int pageSize = 50)
        => Ok(await _users.ListAsync(Role, UserId, role, parentId, Page(page, pageSize)));

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest req) => Ok(await _users.CreateAsync(Role, UserId, req));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UpdateUserRequest req) => Ok(await _users.UpdateAsync(Role, UserId, id, req));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id) { await _users.DeleteAsync(Role, UserId, id); return NoContent(); }

    [HttpGet("tree")]
    public async Task<ActionResult<List<UserTreeNode>>> Tree() => Ok(await _users.TreeAsync(Role, UserId));

    /// <summary>Admin/SuperAdmin activity view for a specific user.</summary>
    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> Activity(int id, DateTime? from, DateTime? to, string? action, int page = 1, int pageSize = 50)
    {
        if (Role < UserRole.Admin) return Forbid();
        return Ok(await _settings.AuditAsync(from, to, id, action, Page(page, pageSize)));
    }
}

[ApiController]
[Route("api/licenses")]
public class LicensesController : ApiControllerBase
{
    private readonly LicenseService _lic;
    public LicensesController(LicenseService lic) => _lic = lic;

    [Authorize(Roles = "Reseller,Admin,SuperAdmin")]
    [HttpGet]
    public async Task<ActionResult<PagedResult<LicenseDto>>> List(string? status, string? type, string? q, int page = 1, int pageSize = 50)
        => Ok(await _lic.ListAsync(Role, UserId, status, type, q, Page(page, pageSize)));

    [Authorize(Roles = "Reseller,Admin,SuperAdmin")]
    [HttpPost]
    public async Task<ActionResult<LicenseDto>> Create(CreateLicenseRequest req) => Ok(await _lic.CreateAsync(Role, UserId, req));

    [Authorize(Roles = "Reseller,Admin,SuperAdmin")]
    [HttpPost("{id:int}/revoke")]
    public async Task<ActionResult<LicenseDto>> Revoke(int id) => Ok(await _lic.RevokeAsync(Role, UserId, id));

    [Authorize(Roles = "Reseller,Admin,SuperAdmin")]
    [HttpGet("{id:int}/activations")]
    public async Task<ActionResult<List<ActivationDto>>> Activations(int id) => Ok(await _lic.ActivationsAsync(id));

    [Authorize(Roles = "Reseller,Admin,SuperAdmin")]
    [HttpDelete("activations/{activationId:int}")]
    public async Task<IActionResult> RevokeDevice(int activationId) { await _lic.RevokeDeviceAsync(activationId); return NoContent(); }

    // ---- public (HWID / offline) ----
    [AllowAnonymous]
    [HttpPost("activate")]
    public async Task<ActionResult<ActivateResponse>> Activate(ActivateRequest req) => Ok(await _lic.ActivateAsync(req));

    [AllowAnonymous]
    [HttpPost("verify")]
    public async Task<ActionResult<VerifyResponse>> Verify(VerifyRequest req) => Ok(await _lic.VerifyAsync(req));

    [AllowAnonymous]
    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate(DeactivateRequest req) { await _lic.DeactivateAsync(req); return NoContent(); }
}
