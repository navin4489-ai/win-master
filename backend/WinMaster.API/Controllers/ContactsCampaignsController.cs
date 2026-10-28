using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WinMaster.Core.Dtos;
using WinMaster.Infrastructure.Services;

namespace WinMaster.API.Controllers;

[ApiController]
[Authorize]
[Route("api/contact-groups")]
public class ContactGroupsController : ApiControllerBase
{
    private readonly ContactService _svc;
    public ContactGroupsController(ContactService svc) => _svc = svc;

    [HttpGet] public async Task<ActionResult<PagedResult<ContactGroupDto>>> List(string? q, string? tag, int page = 1, int pageSize = 50)
        => Ok(await _svc.ListGroupsAsync(UserId, q, tag, Page(page, pageSize)));
    [HttpPost] public async Task<ActionResult<ContactGroupDto>> Create(CreateGroupRequest req) => Ok(await _svc.CreateGroupAsync(UserId, req));
    [HttpPut("{id:int}")] public async Task<ActionResult<ContactGroupDto>> Update(int id, CreateGroupRequest req) => Ok(await _svc.UpdateGroupAsync(UserId, id, req));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) { await _svc.DeleteGroupAsync(UserId, id); return NoContent(); }

    [HttpGet("{id:int}/contacts")] public async Task<ActionResult<PagedResult<ContactDto>>> Contacts(int id, string? q, string? tag, bool? valid, int page = 1, int pageSize = 50)
        => Ok(await _svc.ListContactsAsync(UserId, id, q, tag, valid, Page(page, pageSize)));
    [HttpPost("{id:int}/contacts")] public async Task<ActionResult<ContactDto>> AddContact(int id, CreateContactRequest req) => Ok(await _svc.AddContactAsync(UserId, id, req));

    [HttpPost("{id:int}/import")]
    [RequestSizeLimit(104_857_600)]
    public async Task<ActionResult<ImportResult>> Import(int id, IFormFile file, [FromForm] string? columnMapJson)
    {
        if (file is null || file.Length == 0) return BadRequest(new { title = "No file" });
        using var stream = file.OpenReadStream();
        return Ok(await _svc.ImportAsync(UserId, id, stream, file.FileName, columnMapJson));
    }

    [HttpPost("validate")] public async Task<ActionResult<List<ValidateResult>>> Validate(ValidateRequest req) => Ok(await _svc.ValidateAsync(req.Phones));

    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> Export(int id, string format = "csv")
    { var (bytes, ct, name) = await _svc.ExportAsync(UserId, id, format); return File(bytes, ct, name); }

    [HttpPost("{id:int}/scrape")] public async Task<ActionResult<ScrapeJobDto>> Scrape(int id, ScrapeRequest req) => Ok(await _svc.ScrapeAsync(UserId, id, req));
}

[ApiController]
[Authorize]
[Route("api/contacts")]
public class ContactsController : ApiControllerBase
{
    private readonly ContactService _svc;
    public ContactsController(ContactService svc) => _svc = svc;
    [HttpPut("{id:int}")] public async Task<ActionResult<ContactDto>> Update(int id, CreateContactRequest req) => Ok(await _svc.UpdateContactAsync(UserId, id, req));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) { await _svc.DeleteContactAsync(UserId, id); return NoContent(); }
}

[ApiController]
[Authorize]
[Route("api/tags")]
public class TagsController : ApiControllerBase
{
    private readonly ContactService _svc;
    public TagsController(ContactService svc) => _svc = svc;
    [HttpGet] public async Task<ActionResult<List<TagDto>>> List() => Ok(await _svc.ListTagsAsync(UserId));
    [HttpPost] public async Task<ActionResult<TagDto>> Create(CreateTagRequest req) => Ok(await _svc.CreateTagAsync(UserId, req));
    [HttpPut("{id:int}")] public async Task<ActionResult<TagDto>> Update(int id, CreateTagRequest req) => Ok(await _svc.UpdateTagAsync(UserId, id, req));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) { await _svc.DeleteTagAsync(UserId, id); return NoContent(); }
}

[ApiController]
[Authorize]
[Route("api/campaigns")]
public class CampaignsController : ApiControllerBase
{
    private readonly CampaignService _svc;
    public CampaignsController(CampaignService svc) => _svc = svc;

    [HttpGet] public async Task<ActionResult<PagedResult<CampaignDto>>> List(string? status, string? q, int page = 1, int pageSize = 50)
        => Ok(await _svc.ListAsync(UserId, status, q, Page(page, pageSize)));
    [HttpGet("{id:int}")] public async Task<ActionResult<CampaignDto>> Get(int id) => Ok(await _svc.GetAsync(UserId, id));
    [HttpPost] public async Task<ActionResult<CampaignDto>> Create(CreateCampaignRequest req) => Ok(await _svc.CreateAsync(UserId, req));
    [HttpPut("{id:int}")] public async Task<ActionResult<CampaignDto>> Update(int id, CreateCampaignRequest req) => Ok(await _svc.UpdateAsync(UserId, id, req));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) { await _svc.DeleteAsync(UserId, id); return NoContent(); }
    [HttpPost("{id:int}/schedule")] public async Task<ActionResult<CampaignDto>> Schedule(int id, ScheduleRequest req) => Ok(await _svc.ScheduleAsync(UserId, id, req));
    [HttpPost("{id:int}/start")] public async Task<ActionResult<CampaignDto>> Start(int id) => Ok(await _svc.TransitionAsync(UserId, id, "start"));
    [HttpPost("{id:int}/pause")] public async Task<ActionResult<CampaignDto>> Pause(int id) => Ok(await _svc.TransitionAsync(UserId, id, "pause"));
    [HttpPost("{id:int}/resume")] public async Task<ActionResult<CampaignDto>> Resume(int id) => Ok(await _svc.TransitionAsync(UserId, id, "resume"));
    [HttpPost("{id:int}/stop")] public async Task<ActionResult<CampaignDto>> Stop(int id) => Ok(await _svc.TransitionAsync(UserId, id, "stop"));
    [HttpGet("{id:int}/progress")] public async Task<ActionResult<CampaignProgress>> Progress(int id) => Ok(await _svc.ProgressAsync(UserId, id));
    [HttpGet("{id:int}/recipients")] public async Task<ActionResult<PagedResult<CampaignRecipientDto>>> Recipients(int id, string? status, int page = 1, int pageSize = 50)
        => Ok(await _svc.RecipientsAsync(UserId, id, status, Page(page, pageSize)));
    [HttpGet("{id:int}/report")]
    public async Task<IActionResult> Report(int id, string format = "csv")
    { var (bytes, ct, name) = await _svc.ReportAsync(UserId, id, format); return File(bytes, ct, name); }
}
