using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinMaster.Core.Dtos;
using WinMaster.Infrastructure.Services;

namespace WinMaster.API.Controllers;

[ApiController]
[Authorize]
[Route("api/contact-groups/{groupId:int}/lead-search")]
public class LeadDiscoveryController : ApiControllerBase
{
    private readonly LeadDiscoveryService _svc;
    public LeadDiscoveryController(LeadDiscoveryService svc) => _svc = svc;

    [HttpPost]
    public async Task<ActionResult<LeadSearchResponse>> Search(int groupId, LeadSearchRequest req, CancellationToken ct)
        => Ok(await _svc.SearchAsync(UserId, groupId, req, ct));

    [HttpPost("import")]
    public async Task<ActionResult<LeadImportResult>> Import(int groupId, LeadImportRequest req)
        => Ok(await _svc.ImportAsync(UserId, groupId, req));
}
