using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinMaster.Core.Dtos;
using WinMaster.Core.Interfaces;
using WinMaster.Infrastructure.Services;

namespace WinMaster.API.Controllers;

[ApiController]
[Authorize]
[Route("api/inbox")]
public class InboxController : ApiControllerBase
{
    private readonly InboxService _svc;
    private readonly IWhatsAppChannelFactory _channels;
    public InboxController(InboxService svc, IWhatsAppChannelFactory channels) { _svc = svc; _channels = channels; }

    [HttpGet("threads")] public async Task<ActionResult<PagedResult<InboxThreadDto>>> Threads(int? accountId, string? q, bool unreadOnly = false, int page = 1, int pageSize = 50)
        => Ok(await _svc.ThreadsAsync(UserId, accountId, q, unreadOnly, Page(page, pageSize)));
    [HttpGet("threads/{id:int}/messages")] public async Task<ActionResult<PagedResult<InboxMessageDto>>> Messages(int id, int page = 1, int pageSize = 100)
        => Ok(await _svc.MessagesAsync(UserId, id, Page(page, pageSize)));
    [HttpPost("threads/{id:int}/messages")] public async Task<ActionResult<InboxMessageDto>> Send(int id, SendMessageRequest req)
        => StatusCode(201, await _svc.SendAsync(UserId, id, req, _channels));
    [HttpPut("threads/{id:int}/bot")] public async Task<IActionResult> Bot(int id, BotToggleRequest req) { await _svc.ToggleBotAsync(UserId, id, req.IsBotEnabled); return NoContent(); }
    [HttpPost("threads/{id:int}/read")] public async Task<IActionResult> Read(int id) { await _svc.MarkReadAsync(UserId, id); return NoContent(); }
}

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ApiControllerBase
{
    private readonly ReportService _svc;
    public ReportsController(ReportService svc) => _svc = svc;
    [HttpGet("delivery")] public async Task<ActionResult<DeliveryReport>> Delivery(DateTime? from, DateTime? to, int? campaignId)
        => Ok(await _svc.DeliveryAsync(UserId, from, to, campaignId));
    [HttpGet("kpis")] public async Task<ActionResult<KpiReport>> Kpis(DateTime? from, DateTime? to) => Ok(await _svc.KpisAsync(UserId, from, to));
}

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController : ApiControllerBase
{
    private readonly ReportService _svc;
    public DashboardController(ReportService svc) => _svc = svc;
    [HttpGet("stats")] public async Task<ActionResult<DashboardStats>> Stats() => Ok(await _svc.DashboardAsync(UserId));
}

[ApiController]
[Authorize]
[Route("api")]
public class SettingsController : ApiControllerBase
{
    private readonly SettingsService _svc;
    public SettingsController(SettingsService svc) => _svc = svc;

    [HttpGet("settings")] public async Task<ActionResult<Dictionary<string, System.Text.Json.JsonElement>>> Get() => Ok(await _svc.GetAsync());
    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPut("settings")] public async Task<IActionResult> Put(Dictionary<string, System.Text.Json.JsonElement> body) { await _svc.PutAsync(body); return NoContent(); }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet("audit-logs")] public async Task<ActionResult<PagedResult<AuditLogDto>>> Audit(DateTime? from, DateTime? to, int? userId, string? action, int page = 1, int pageSize = 50)
        => Ok(await _svc.AuditAsync(from, to, userId, action, Page(page, pageSize)));
}

[ApiController]
[Authorize]
[Route("api/push-tokens")]
public class PushTokensController : ApiControllerBase
{
    private readonly PushTokenService _svc;
    public PushTokensController(PushTokenService svc) => _svc = svc;
    [HttpPost] public async Task<IActionResult> Register(PushTokenRequest req) { await _svc.RegisterAsync(UserId, req); return NoContent(); }
    [HttpDelete("{token}")] public async Task<IActionResult> Remove(string token) { await _svc.RemoveAsync(token); return NoContent(); }
}

[ApiController]
[Route("api")]
public class IntegrationsController : ControllerBase
{
    private readonly BridgeService _bridge;
    private readonly IConfiguration _cfg;
    public IntegrationsController(BridgeService bridge, IConfiguration cfg) { _bridge = bridge; _cfg = cfg; }

    [AllowAnonymous]
    [HttpPost("bridge/events")]
    public async Task<IActionResult> BridgeEvents([FromHeader(Name = "X-Bridge-Key")] string? key, BridgeEvent ev)
    {
        var expected = _cfg["Bridge:Key"] ?? "dev-bridge-key";
        if (key != expected) return Unauthorized();
        await _bridge.HandleAsync(ev);
        return Ok(new { received = true });
    }

    [AllowAnonymous]
    [HttpGet("webhooks/meta")]
    public IActionResult Verify([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? token, [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var verify = _cfg["Meta:VerifyToken"] ?? "winmaster-verify";
        if (mode == "subscribe" && token == verify) return Content(challenge ?? "");
        return Forbid();
    }

    /// <summary>Diagnostic: pings the co-hosted WhatsApp bridge and reports whether it is reachable.</summary>
    [AllowAnonymous]
    [HttpGet("bridge/health")]
    public async Task<IActionResult> BridgeHealth([FromServices] IHttpClientFactory httpFactory)
    {
        var baseUrl = (_cfg["Bridge:BaseUrl"] ?? "http://localhost:3001").TrimEnd('/');
        try
        {
            var client = httpFactory.CreateClient("bridge");
            client.Timeout = TimeSpan.FromSeconds(5);
            var resp = await client.GetAsync($"{baseUrl}/healthz");
            var body = await resp.Content.ReadAsStringAsync();
            return Ok(new { baseUrl, reachable = resp.IsSuccessStatusCode, status = (int)resp.StatusCode, body });
        }
        catch (Exception ex)
        {
            return Ok(new { baseUrl, reachable = false, error = ex.GetType().Name, message = ex.Message });
        }
    }

    /// <summary>Diagnostic: sends a message straight through the bridge and returns the RAW result
    /// (real WhatsApp message id or the exact error) — no worker, no simulation fallback.</summary>
    [AllowAnonymous]
    [HttpGet("bridge/testsend")]
    public async Task<IActionResult> BridgeTestSend([FromServices] IHttpClientFactory httpFactory,
        [FromQuery] int accountId, [FromQuery] string to, [FromQuery] string? text)
    {
        var baseUrl = (_cfg["Bridge:BaseUrl"] ?? "http://localhost:3001").TrimEnd('/');
        var key = _cfg["Bridge:Key"] ?? "dev-bridge-key";
        try
        {
            var client = httpFactory.CreateClient("bridge");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Remove("X-Bridge-Key");
            client.DefaultRequestHeaders.Add("X-Bridge-Key", key);
            var payload = new { to, text = text ?? "diagnostic test", mediaUrl = (string?)null, mediaType = "None" };
            var resp = await client.PostAsJsonAsync($"{baseUrl}/sessions/{accountId}/send", payload);
            var body = await resp.Content.ReadAsStringAsync();
            return Ok(new { sentTo = to, httpStatus = (int)resp.StatusCode, ok = resp.IsSuccessStatusCode, rawBridgeResponse = body });
        }
        catch (Exception ex)
        {
            return Ok(new { sentTo = to, ok = false, error = ex.GetType().Name, message = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpPost("webhooks/meta")]
    public IActionResult MetaWebhook() => Ok(new { received = true });
}
