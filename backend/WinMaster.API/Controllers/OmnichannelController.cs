using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinMaster.Core.Dtos;
using WinMaster.Infrastructure.Services;

namespace WinMaster.API.Controllers;

[ApiController]
[Authorize]
[Route("api/omnichannel")]
public class OmnichannelController : ApiControllerBase
{
    private readonly OmnichannelService _svc;
    public OmnichannelController(OmnichannelService svc) => _svc = svc;

    [HttpGet("accounts")]
    public async Task<ActionResult<List<ChannelAccountDto>>> Accounts() => Ok(await _svc.AccountsAsync(UserId));

    [HttpPost("accounts")]
    public async Task<ActionResult<ChannelAccountDto>> CreateAccount(CreateChannelAccountRequest req) => Ok(await _svc.CreateAccountAsync(UserId, req));

    [HttpDelete("accounts/{id:int}")]
    public async Task<IActionResult> DeleteAccount(int id) { await _svc.DeleteAccountAsync(UserId, id); return NoContent(); }

    [HttpPost("send")]
    public async Task<ActionResult<InboxMessageDto>> Send(SendChannelMessageRequest req) => StatusCode(201, await _svc.SendAsync(UserId, req));

    [AllowAnonymous]
    [HttpPost("webhooks/{channel}")]
    public async Task<IActionResult> Webhook(string channel, ChannelWebhookRequest req)
    {
        if (!string.Equals(channel, req.Channel, StringComparison.OrdinalIgnoreCase)) return BadRequest(new { detail = "Channel mismatch." });
        await _svc.HandleWebhookAsync(req);
        return Ok(new { received = true });
    }

    [HttpGet("leads")]
    public async Task<ActionResult<PagedResult<LeadDto>>> Leads(string? channel, string? status, int page = 1, int pageSize = 50)
        => Ok(await _svc.LeadsAsync(UserId, channel, status, Page(page, pageSize)));

    [HttpPut("leads/{id:int}")]
    public async Task<ActionResult<LeadDto>> UpdateLead(int id, UpdateLeadRequest req) => Ok(await _svc.UpdateLeadAsync(UserId, id, req));

    [HttpGet("lead-analytics")]
    public async Task<ActionResult<LeadAnalytics>> Analytics() => Ok(await _svc.AnalyticsAsync(UserId));

    [HttpPost("conversations/{threadId:int}/analyze")]
    public async Task<ActionResult<AiConversationAnalysis>> Analyze(int threadId) => Ok(await _svc.AnalyzeConversationAsync(UserId, threadId));
}
