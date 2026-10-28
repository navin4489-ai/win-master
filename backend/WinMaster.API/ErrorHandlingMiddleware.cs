using System.Text.Json;
using WinMaster.Infrastructure.Services;

namespace WinMaster.API;

/// <summary>Maps domain exceptions to RFC-7807 problem responses and masks sensitive data.</summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _log;
    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log) { _next = next; _log = log; }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (Exception ex)
        {
            var (status, title) = ex switch
            {
                NotFoundException => (404, "Not Found"),
                ForbiddenException => (403, "Forbidden"),
                ConflictException => (409, "Conflict"),
                BusinessException => (422, "Unprocessable Entity"),
                _ => (500, "Server Error")
            };
            if (status == 500) _log.LogError(ex, "Unhandled error");
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                title,
                status,
                detail = ex.Message
            }));
        }
    }
}
