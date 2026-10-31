using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WinMaster.Core;
using WinMaster.Core.Dtos;
using WinMaster.Core.Entities;
using WinMaster.Core.Services;
using WinMaster.Infrastructure.Data;

namespace WinMaster.Infrastructure.Services;

/// <summary>
/// Discovers public business listings through a configured compliant provider.
/// AI may plan/rank the query, but it never invents contact data.
/// </summary>
public class LeadDiscoveryService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    public LeadDiscoveryService(AppDbContext db, IHttpClientFactory http, IConfiguration config)
    { _db = db; _http = http; _config = config; }

    public async Task<LeadSearchResponse> SearchAsync(int ownerId, int groupId, LeadSearchRequest req, CancellationToken ct = default)
    {
        await OwnGroupAsync(ownerId, groupId);
        if (string.IsNullOrWhiteSpace(req.Pincode) || req.Pincode.Trim().Length != 6 || !req.Pincode.All(char.IsDigit))
            throw new BusinessException("Enter a valid 6-digit Indian PIN code.");
        if (req.LeadCount is < 1 or > 100) throw new BusinessException("Lead count must be between 1 and 100.");
        if (string.IsNullOrWhiteSpace(req.BusinessType)) throw new BusinessException("Select a business type.");

        var apiKey = _config["LeadDiscovery:ApiKey"] ?? Environment.GetEnvironmentVariable("LEAD_DISCOVERY_API_KEY");
        var query = string.IsNullOrWhiteSpace(req.Keyword)
            ? $"{req.BusinessType} near {req.Pincode}, India"
            : $"{req.Keyword} {req.BusinessType} near {req.Pincode}, India";
        if (string.IsNullOrWhiteSpace(apiKey))
            return new LeadSearchResponse("GooglePlaces", query, new(), "Lead discovery is not configured. Add LeadDiscovery:ApiKey (Google Places API) to the API environment before searching.");

        var client = _http.CreateClient("lead-discovery");
        var searchUrl = $"https://maps.googleapis.com/maps/api/place/textsearch/json?query={Uri.EscapeDataString(query)}&key={Uri.EscapeDataString(apiKey)}";
        using var search = await client.GetAsync(searchUrl, ct);
        var searchJson = await search.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!search.IsSuccessStatusCode) throw new BusinessException($"Lead provider returned {(int)search.StatusCode}.");
        var status = searchJson.TryGetProperty("status", out var s) ? s.GetString() : "";
        if (status is not ("OK" or "ZERO_RESULTS"))
            throw new BusinessException($"Lead provider error: {status}.");

        var results = new List<LeadSearchResultDto>();
        if (searchJson.TryGetProperty("results", out var rows))
        {
            foreach (var row in rows.EnumerateArray().Take(req.LeadCount))
            {
                var placeId = Get(row, "place_id") ?? Guid.NewGuid().ToString("N");
                var details = await GetDetailsAsync(client, apiKey, placeId, ct);
                var phone = NormalizeIndiaPhone(details?.GetValueOrDefault("international_phone_number") ?? details?.GetValueOrDefault("formatted_phone_number"));
                if (string.IsNullOrWhiteSpace(phone)) continue; // never invent a phone number
                results.Add(new LeadSearchResultDto(
                    placeId,
                    Get(row, "name") ?? "Unnamed business",
                    null,
                    phone,
                    Get(row, "formatted_address"),
                    "Google Places",
                    $"https://www.google.com/maps/search/?api=1&query=Google&query_place_id={Uri.EscapeDataString(placeId)}",
                    0.85m,
                    true));
            }
        }
        return new LeadSearchResponse("GooglePlaces", query, results, results.Count == 0 ? "No public phone numbers were returned for this search." : null);
    }

    public async Task<LeadImportResult> ImportAsync(int ownerId, int groupId, LeadImportRequest req)
    {
        var group = await OwnGroupAsync(ownerId, groupId);
        var existing = (await _db.Contacts.Where(c => c.GroupId == groupId).Select(c => c.Phone).ToListAsync()).ToHashSet();
        var imported = 0; var duplicates = 0; var invalid = 0; var errors = new List<string>();
        foreach (var lead in req.Leads.Where(x => x.Selected))
        {
            var phone = NormalizeIndiaPhone(lead.Phone);
            if (phone is null) { invalid++; continue; }
            if (!existing.Add(phone)) { duplicates++; continue; }
            _db.Contacts.Add(new Contact
            {
                GroupId = groupId, Phone = phone, Name = string.IsNullOrWhiteSpace(lead.OwnerName) ? lead.BusinessName : lead.OwnerName,
                WaRegistered = null, CustomFields = JsonSerializer.Serialize(new { businessName = lead.BusinessName, address = lead.Address, source = lead.Source, sourceUrl = lead.SourceUrl, confidence = lead.Confidence }),
                Tags = "ai-lead"
            });
            imported++;
        }
        group.TotalContacts = await _db.Contacts.CountAsync(c => c.GroupId == groupId) + imported;
        group.Source = ContactSource.Import;
        await _db.SaveChangesAsync();
        return new LeadImportResult(imported, duplicates, invalid, errors);
    }

    private async Task<Dictionary<string, string>?> GetDetailsAsync(HttpClient client, string key, string placeId, CancellationToken ct)
    {
        var url = $"https://maps.googleapis.com/maps/api/place/details/json?place_id={Uri.EscapeDataString(placeId)}&fields=name,formatted_phone_number,international_phone_number&key={Uri.EscapeDataString(key)}";
        var response = await client.GetFromJsonAsync<JsonElement>(url, ct);
        if (!response.TryGetProperty("result", out var result)) return null;
        var dict = new Dictionary<string, string>();
        foreach (var name in new[] { "formatted_phone_number", "international_phone_number" })
            if (result.TryGetProperty(name, out var value)) dict[name] = value.GetString() ?? "";
        return dict;
    }

    private async Task<ContactGroup> OwnGroupAsync(int ownerId, int groupId) =>
        await _db.ContactGroups.FirstOrDefaultAsync(g => g.Id == groupId && g.OwnerId == ownerId)
        ?? throw new NotFoundException("Contact group not found.");

    public static string? NormalizeIndiaPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0091")) digits = digits[4..];
        if (digits.StartsWith("91") && digits.Length == 12) digits = digits[2..];
        if (digits.Length == 10 && digits[0] is >= '6' and <= '9') return "+91" + digits;
        if (value.StartsWith("+") && digits.Length >= 10 && digits.Length <= 15) return "+" + digits;
        return null;
    }

    private static string? Get(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;
}
