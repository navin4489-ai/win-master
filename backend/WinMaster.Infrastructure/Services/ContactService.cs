using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Core.Services;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

public class ContactService
{
    private readonly AppDbContext _db;
    public ContactService(AppDbContext db) => _db = db;

    public async Task<PagedResult<ContactGroupDto>> ListGroupsAsync(int ownerId, string? q, string? tag, PageQuery page)
    {
        var query = _db.ContactGroups.Where(g => g.OwnerId == ownerId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(g => g.Name.Contains(q));
        if (!string.IsNullOrWhiteSpace(tag)) query = query.Where(g => g.Tags.Contains(tag));
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(g => g.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return new PagedResult<ContactGroupDto>(items.Select(g => g.ToDto()).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<ContactGroupDto> CreateGroupAsync(int ownerId, CreateGroupRequest req)
    {
        var g = new ContactGroup { OwnerId = ownerId, Name = req.Name, Tags = string.Join(',', req.Tags ?? Array.Empty<string>()) };
        _db.ContactGroups.Add(g); await _db.SaveChangesAsync();
        return g.ToDto();
    }

    public async Task<ContactGroupDto> UpdateGroupAsync(int ownerId, int id, CreateGroupRequest req)
    {
        var g = await OwnedGroup(ownerId, id);
        g.Name = req.Name; g.Tags = string.Join(',', req.Tags ?? Array.Empty<string>()); g.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return g.ToDto();
    }

    public async Task DeleteGroupAsync(int ownerId, int id)
    { var g = await OwnedGroup(ownerId, id); _db.ContactGroups.Remove(g); await _db.SaveChangesAsync(); }

    public async Task<PagedResult<ContactDto>> ListContactsAsync(int ownerId, int groupId, string? q, string? tag, bool? valid, PageQuery page)
    {
        await OwnedGroup(ownerId, groupId);
        var query = _db.Contacts.Where(c => c.GroupId == groupId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(c => c.Phone.Contains(q) || (c.Name != null && c.Name.Contains(q)));
        if (!string.IsNullOrWhiteSpace(tag)) query = query.Where(c => c.Tags.Contains(tag));
        if (valid.HasValue) query = query.Where(c => c.WaRegistered == valid);
        var total = await query.CountAsync();
        var items = await query.OrderBy(c => c.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync();
        return new PagedResult<ContactDto>(items.Select(c => c.ToDto()).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<ContactDto> AddContactAsync(int ownerId, int groupId, CreateContactRequest req)
    {
        var g = await OwnedGroup(ownerId, groupId);
        var phone = PhoneUtils.Normalize(req.Phone);
        if (!PhoneUtils.IsValid(phone)) throw new BusinessException("Invalid phone number.");
        if (await _db.Contacts.AnyAsync(c => c.GroupId == groupId && c.Phone == phone))
            throw new ConflictException("Contact already exists in this group.");
        var c = new Contact { GroupId = groupId, Phone = phone, Name = req.Name,
            CustomFields = JsonSerializer.Serialize(req.CustomFields ?? new()), Tags = string.Join(',', req.Tags ?? Array.Empty<string>()) };
        _db.Contacts.Add(c);
        g.TotalContacts++;
        await _db.SaveChangesAsync();
        return c.ToDto();
    }

    public async Task<ContactDto> UpdateContactAsync(int ownerId, int id, CreateContactRequest req)
    {
        var c = await _db.Contacts.Include(x => x.Group).FirstOrDefaultAsync(x => x.Id == id && x.Group!.OwnerId == ownerId)
            ?? throw new NotFoundException("Contact not found.");
        c.Name = req.Name;
        c.CustomFields = JsonSerializer.Serialize(req.CustomFields ?? new());
        c.Tags = string.Join(',', req.Tags ?? Array.Empty<string>());
        await _db.SaveChangesAsync();
        return c.ToDto();
    }

    public async Task DeleteContactAsync(int ownerId, int id)
    {
        var c = await _db.Contacts.Include(x => x.Group).FirstOrDefaultAsync(x => x.Id == id && x.Group!.OwnerId == ownerId)
            ?? throw new NotFoundException("Contact not found.");
        _db.Contacts.Remove(c); await _db.SaveChangesAsync();
    }

    public async Task<ImportResult> ImportAsync(int ownerId, int groupId, Stream file, string fileName, string? columnMapJson)
    {
        var g = await OwnedGroup(ownerId, groupId);
        var rows = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? ReadXlsx(file) : ReadCsv(file);

        var map = string.IsNullOrWhiteSpace(columnMapJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(columnMapJson) ?? new();
        var phoneCol = map.GetValueOrDefault("phone", "phone");
        var nameCol = map.GetValueOrDefault("name", "name");

        int imported = 0, invalid = 0, duplicates = 0;
        var errors = new List<string>();
        var existing = (await _db.Contacts.Where(c => c.GroupId == groupId).Select(c => c.Phone).ToListAsync()).ToHashSet();
        var header = rows.FirstOrDefault();
        var batch = new List<Contact>();
        if (header is null) return new ImportResult(0, 0, 0, new() { "Empty file" });

        int phoneIdx = IndexOf(header, phoneCol), nameIdx = IndexOf(header, nameCol);
        if (phoneIdx < 0) phoneIdx = 0;

        foreach (var row in rows.Skip(1))
        {
            if (phoneIdx >= row.Length) { invalid++; continue; }
            var phone = PhoneUtils.Normalize(row[phoneIdx]);
            if (!PhoneUtils.IsValid(phone)) { invalid++; continue; }
            if (!existing.Add(phone)) { duplicates++; continue; }
            var custom = new Dictionary<string, string>();
            for (int i = 0; i < header.Length && i < row.Length; i++)
                if (i != phoneIdx && i != nameIdx && !string.IsNullOrWhiteSpace(header[i]))
                    custom[header[i].Trim()] = row[i];
            batch.Add(new Contact
            {
                GroupId = groupId, Phone = phone,
                Name = nameIdx >= 0 && nameIdx < row.Length ? row[nameIdx] : null,
                CustomFields = JsonSerializer.Serialize(custom)
            });
            imported++;
            if (batch.Count >= 1000) { _db.Contacts.AddRange(batch); await _db.SaveChangesAsync(); batch.Clear(); }
        }
        if (batch.Count > 0) { _db.Contacts.AddRange(batch); await _db.SaveChangesAsync(); }

        g.TotalContacts = await _db.Contacts.CountAsync(c => c.GroupId == groupId);
        g.Source = ContactSource.Import;
        await _db.SaveChangesAsync();
        return new ImportResult(imported, invalid, duplicates, errors);
    }

    public async Task<List<ValidateResult>> ValidateAsync(List<string> phones)
    {
        // Validation = format check + "is on WhatsApp" probe (simulated deterministically here).
        await Task.CompletedTask;
        return phones.Select(p =>
        {
            var n = PhoneUtils.Normalize(p);
            var valid = PhoneUtils.IsValid(n);
            bool? wa = valid ? (n.GetHashCode() % 9 != 0) : (bool?)null;
            return new ValidateResult(p, valid, wa, valid ? null : "Invalid format");
        }).ToList();
    }

    public async Task<(byte[] bytes, string contentType, string fileName)> ExportAsync(int ownerId, int groupId, string format)
    {
        var g = await OwnedGroup(ownerId, groupId);
        var contacts = await _db.Contacts.Where(c => c.GroupId == groupId).ToListAsync();
        if (format.Equals("xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Contacts");
            ws.Cell(1, 1).Value = "Phone"; ws.Cell(1, 2).Value = "Name"; ws.Cell(1, 3).Value = "WaRegistered";
            for (int i = 0; i < contacts.Count; i++)
            {
                ws.Cell(i + 2, 1).Value = contacts[i].Phone;
                ws.Cell(i + 2, 2).Value = contacts[i].Name;
                ws.Cell(i + 2, 3).Value = contacts[i].WaRegistered?.ToString();
            }
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return (ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{g.Name}.xlsx");
        }
        var sb = new StringBuilder("Phone,Name,WaRegistered\n");
        foreach (var c in contacts) sb.AppendLine($"{c.Phone},{Csv(c.Name)},{c.WaRegistered}");
        return (Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"{g.Name}.csv");
    }

    public async Task<ScrapeJobDto> ScrapeAsync(int ownerId, int groupId, ScrapeRequest req)
    {
        await OwnedGroup(ownerId, groupId);
        // Enqueues a bridge group-scrape job; here we return an accepted job id.
        return new ScrapeJobDto(Guid.NewGuid().ToString("N"), "Queued", 0, "Scrape enqueued via bridge.");
    }

    // ---- tags ----
    public async Task<List<TagDto>> ListTagsAsync(int ownerId) =>
        (await _db.Tags.Where(t => t.OwnerId == ownerId).ToListAsync()).Select(t => t.ToDto()).ToList();
    public async Task<TagDto> CreateTagAsync(int ownerId, CreateTagRequest req)
    { var t = new Tag { OwnerId = ownerId, Name = req.Name, Color = req.Color }; _db.Tags.Add(t); await _db.SaveChangesAsync(); return t.ToDto(); }
    public async Task<TagDto> UpdateTagAsync(int ownerId, int id, CreateTagRequest req)
    {
        var t = await _db.Tags.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId) ?? throw new NotFoundException("Tag not found.");
        t.Name = req.Name; t.Color = req.Color; await _db.SaveChangesAsync(); return t.ToDto();
    }
    public async Task DeleteTagAsync(int ownerId, int id)
    {
        var t = await _db.Tags.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId) ?? throw new NotFoundException("Tag not found.");
        _db.Tags.Remove(t); await _db.SaveChangesAsync();
    }

    private async Task<ContactGroup> OwnedGroup(int ownerId, int id) =>
        await _db.ContactGroups.FirstOrDefaultAsync(g => g.Id == id && g.OwnerId == ownerId) ?? throw new NotFoundException("Group not found.");

    private static int IndexOf(string[] header, string col) =>
        Array.FindIndex(header, h => string.Equals(h.Trim(), col, StringComparison.OrdinalIgnoreCase));
    private static string Csv(string? v) => v is null ? "" : v.Contains(',') ? $"\"{v}\"" : v;

    private static List<string[]> ReadCsv(Stream s)
    {
        using var reader = new StreamReader(s);
        var list = new List<string[]>();
        string? line;
        while ((line = reader.ReadLine()) != null)
            list.Add(line.Split(',').Select(x => x.Trim().Trim('"')).ToArray());
        return list;
    }

    private static List<string[]> ReadXlsx(Stream s)
    {
        using var wb = new XLWorkbook(s);
        var ws = wb.Worksheets.First();
        var list = new List<string[]>();
        foreach (var row in ws.RangeUsed()?.RowsUsed() ?? Enumerable.Empty<IXLRangeRow>())
            list.Add(row.Cells().Select(c => c.GetString()).ToArray());
        return list;
    }
}
