using System.Security.Claims;
using WinMaster.Core.Entities;
using WinMaster.Infrastructure.Data;

namespace WinMaster.API;

/// <summary>Records authenticated API activity for Admin/SuperAdmin review.</summary>
public sealed class ActivityAuditMiddleware
{
    private readonly RequestDelegate _next;
    public ActivityAuditMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        await _next(context);
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (context.Request.Path.StartsWithSegments("/api/auth") ||
            context.Request.Path.StartsWithSegments("/healthz")) return;

        var idValue = context.User.FindFirstValue("uid") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idValue, out var userId)) return;
        var detail = $"{context.Request.Method} {context.Request.Path} -> {context.Response.StatusCode}";
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = context.Request.Method,
            Entity = context.Request.Path.Value,
            MaskedDetail = detail,
            Ip = context.Connection.RemoteIpAddress?.ToString()
        });
        try { await db.SaveChangesAsync(); } catch { /* activity logging must never break requests */ }
    }
}
