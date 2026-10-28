using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WinMaster.Core;

namespace WinMaster.API.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected int UserId => int.TryParse(User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    protected string? Email => User.FindFirstValue(ClaimTypes.Email);
    protected UserRole Role => Enum.TryParse<UserRole>(User.FindFirstValue(ClaimTypes.Role), out var r) ? r : UserRole.User;
    protected WinMaster.Core.Dtos.PageQuery Page(int page, int pageSize) =>
        new(page < 1 ? 1 : page, pageSize is < 1 or > 500 ? 50 : pageSize);
}
