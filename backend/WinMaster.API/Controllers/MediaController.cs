using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace WinMaster.API.Controllers;

[ApiController]
[Authorize]
[Route("api/media")]
public class MediaController : ApiControllerBase
{
    private readonly IWebHostEnvironment _env;
    public MediaController(IWebHostEnvironment env) => _env = env;

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".mp4", ".3gp", ".mov",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv",
        ".mp3", ".ogg", ".m4a", ".aac",
    };

    /// <summary>Uploads a media file and returns a public URL usable as a template/campaign attachment.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { title = "No file", detail = "Choose a file to upload." });

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !Allowed.Contains(ext))
            return BadRequest(new { title = "Unsupported file", detail = $"'{ext}' is not an allowed file type." });

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var dir = Path.Combine(webRoot, "uploads");
        Directory.CreateDirectory(dir);

        var name = $"{UserId}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var fullPath = Path.Combine(dir, name);
        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        // Absolute URL so the WhatsApp bridge can fetch it directly.
        var url = $"{Request.Scheme}://{Request.Host}/uploads/{name}";
        return Ok(new { url, fileName = file.FileName });
    }
}
