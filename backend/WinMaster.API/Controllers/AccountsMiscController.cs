using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinMaster.Core.Dtos;
using WinMaster.Infrastructure.Services;

namespace WinMaster.API.Controllers;

[ApiController]
[Authorize]
[Route("api/whatsapp-accounts")]
public class WhatsAppAccountsController : ApiControllerBase
{
    private readonly AccountService _svc;
    public WhatsAppAccountsController(AccountService svc) => _svc = svc;

    [HttpGet] public async Task<ActionResult<List<WaAccountDto>>> List() => Ok(await _svc.ListAsync(UserId));
    [HttpGet("status")] public async Task<ActionResult<AccountsStatusDto>> Status() => Ok(await _svc.StatusAsync(UserId));
    [HttpGet("{id:int}")] public async Task<ActionResult<WaAccountDto>> Get(int id) => Ok(await _svc.GetAsync(UserId, id));
    [HttpPost] public async Task<ActionResult<WaAccountDto>> Create(CreateWaAccountRequest req) => Ok(await _svc.CreateAsync(UserId, req));
    [HttpPut("{id:int}")] public async Task<ActionResult<WaAccountDto>> Update(int id, UpdateWaAccountRequest req) => Ok(await _svc.UpdateAsync(UserId, id, req));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) { await _svc.DeleteAsync(UserId, id); return NoContent(); }
    [HttpPost("{id:int}/connect")] public async Task<ActionResult<WaAccountDto>> Connect(int id) => Ok(await _svc.ConnectAsync(UserId, id));
    [HttpGet("{id:int}/qr")] public async Task<ActionResult<QrResponse>> Qr(int id) => Ok(await _svc.QrAsync(UserId, id));
    [HttpPost("{id:int}/disconnect")] public async Task<IActionResult> Disconnect(int id) { await _svc.DisconnectAsync(UserId, id); return NoContent(); }
    [HttpPost("{id:int}/rotate")] public async Task<ActionResult<WaAccountDto>> Rotate(int id, [FromBody] RotateBody body) => Ok(await _svc.RotateAsync(UserId, id, body.RotationWeight));
    public record RotateBody(int RotationWeight);
}

[ApiController]
[Authorize]
[Route("api/proxies")]
public class ProxiesController : ApiControllerBase
{
    private readonly ProxyService _svc;
    public ProxiesController(ProxyService svc) => _svc = svc;

    [HttpGet] public async Task<ActionResult<List<ProxyDto>>> List() => Ok(await _svc.ListAsync(UserId));
    [HttpPost] public async Task<ActionResult<ProxyDto>> Create(CreateProxyRequest req) => Ok(await _svc.CreateAsync(UserId, req));
    [HttpPut("{id:int}")] public async Task<ActionResult<ProxyDto>> Update(int id, CreateProxyRequest req) => Ok(await _svc.UpdateAsync(UserId, id, req));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) { await _svc.DeleteAsync(UserId, id); return NoContent(); }
    [HttpPost("{id:int}/test")] public async Task<ActionResult<ProxyTestResult>> Test(int id) => Ok(await _svc.TestAsync(UserId, id));
}

[ApiController]
[Authorize]
[Route("api/templates")]
public class TemplatesController : ApiControllerBase
{
    private readonly TemplateService _svc;
    public TemplatesController(TemplateService svc) => _svc = svc;

    [HttpGet] public async Task<ActionResult<List<TemplateDto>>> List() => Ok(await _svc.ListAsync(UserId));
    [HttpPost] public async Task<ActionResult<TemplateDto>> Create(CreateTemplateRequest req) => Ok(await _svc.CreateAsync(UserId, req));
    [HttpPut("{id:int}")] public async Task<ActionResult<TemplateDto>> Update(int id, CreateTemplateRequest req) => Ok(await _svc.UpdateAsync(UserId, id, req));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) { await _svc.DeleteAsync(UserId, id); return NoContent(); }
}

[ApiController]
[Authorize]
[Route("api/anti-ban")]
public class AntiBanController : ApiControllerBase
{
    private readonly AntiBanService _svc;
    public AntiBanController(AntiBanService svc) => _svc = svc;
    [HttpGet("{accountId:int}")] public async Task<ActionResult<AntiBanDto>> Get(int accountId) => Ok(await _svc.GetAsync(accountId));
    [HttpPut("{accountId:int}")] public async Task<ActionResult<AntiBanDto>> Update(int accountId, AntiBanDto dto) => Ok(await _svc.UpdateAsync(accountId, dto));
}

[ApiController]
[Authorize]
[Route("api/auto-reply-rules")]
public class AutoReplyRulesController : ApiControllerBase
{
    private readonly RuleService _svc;
    public AutoReplyRulesController(RuleService svc) => _svc = svc;
    [HttpGet] public async Task<ActionResult<List<AutoReplyRuleDto>>> List(int? accountId) => Ok(await _svc.ListAsync(UserId, accountId));
    [HttpPost] public async Task<ActionResult<AutoReplyRuleDto>> Create(CreateAutoReplyRuleRequest req) => Ok(await _svc.CreateAsync(UserId, req));
    [HttpPut("{id:int}")] public async Task<ActionResult<AutoReplyRuleDto>> Update(int id, CreateAutoReplyRuleRequest req) => Ok(await _svc.UpdateAsync(UserId, id, req));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) { await _svc.DeleteAsync(UserId, id); return NoContent(); }
    [HttpPut("{id:int}/toggle")] public async Task<ActionResult<AutoReplyRuleDto>> Toggle(int id) => Ok(await _svc.ToggleAsync(UserId, id));
}

[ApiController]
[Authorize]
[Route("api/ai-settings")]
public class AiSettingsController : ApiControllerBase
{
    private readonly AiService _svc;
    public AiSettingsController(AiService svc) => _svc = svc;
    [HttpGet] public async Task<ActionResult<AiSettingsDto>> Get() => Ok(await _svc.GetAsync(UserId));
    [HttpPut] public async Task<ActionResult<AiSettingsDto>> Update(UpdateAiSettingsRequest req) => Ok(await _svc.UpdateAsync(UserId, req));
    [HttpPost("test")] public async Task<ActionResult<AiTestResponse>> Test(AiTestRequest req) => Ok(await _svc.TestAsync(UserId, req));
}
