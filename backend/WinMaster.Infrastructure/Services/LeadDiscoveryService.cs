using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly ConcurrentDictionary<string, (DateTime Expires, LeadSearchResponse Response)> Cache = new();
    private static readonly SemaphoreSlim SearchGate = new(2, 2);
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

        var query = string.IsNullOrWhiteSpace(req.Keyword)
            ? $"{req.BusinessType} near {req.Pincode}, India"
            : $"{req.Keyword} {req.BusinessType} near {req.Pincode}, India";
        var cacheKey = $"{req.Pincode}|{req.LeadCount}|{req.BusinessType}|{req.Keyword}".ToLowerInvariant();
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.Expires > DateTime.UtcNow) return cached.Response;
        var apiKey = _config["LeadDiscovery:ApiKey"] ?? Environment.GetEnvironmentVariable("LEAD_DISCOVERY_API_KEY");
        var webKey = _config["LeadDiscovery:WebSearchApiKey"] ?? Environment.GetEnvironmentVariable("LEAD_WEB_SEARCH_API_KEY");
        await SearchGate.WaitAsync(ct);
        try
        {
            LeadSearchResponse response;
            try
            {
                if (!string.IsNullOrWhiteSpace(webKey)) response = await SearchBraveAsync(query, req, webKey, ct);
                else if (string.IsNullOrWhiteSpace(apiKey)) response = await SearchOpenStreetMapAsync(query, req, ct);
                else response = await SearchGoogleAsync(query, req, apiKey, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                response = new LeadSearchResponse("OpenStreetMap", query, new(), "The free search provider timed out. Please try again with a broader business type or PIN code.");
            }
            catch (HttpRequestException)
            {
                response = new LeadSearchResponse("OpenStreetMap", query, new(), "The free search provider is temporarily unavailable. Please try again shortly.");
            }
            Cache[cacheKey] = (DateTime.UtcNow.AddMinutes(10), response);
            return response;
        }
        finally { SearchGate.Release(); }
    }

    private async Task<LeadSearchResponse> SearchGoogleAsync(string query, LeadSearchRequest req, string apiKey, CancellationToken ct)
    {

        var client = _http.CreateClient("lead-discovery");
        client.Timeout = TimeSpan.FromSeconds(20);
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

    /// <summary>Optional Brave Web Search free-tier integration. It only accepts
    /// phone numbers explicitly present in public result snippets.</summary>
    private async Task<LeadSearchResponse> SearchBraveAsync(string query, LeadSearchRequest req, string apiKey, CancellationToken ct)
    {
        var client = _http.CreateClient("lead-discovery");
        client.Timeout = TimeSpan.FromSeconds(20);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={Math.Min(req.LeadCount * 2, 50)}&country=IN");
        request.Headers.Add("X-Subscription-Token", apiKey);
        request.Headers.Add("Accept", "application/json");
        using var response = await SendWithRetryAsync(client, request, ct);
        if (response is null || !response.IsSuccessStatusCode)
            return new LeadSearchResponse("BraveSearch", query, new(), "Web search is temporarily rate-limited. Please try again shortly.");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var results = new List<LeadSearchResultDto>();
        if (json.TryGetProperty("web", out var web) && web.TryGetProperty("results", out var rows))
        {
            foreach (var row in rows.EnumerateArray())
            {
                var title = Get(row, "title") ?? "Business listing";
                var description = Get(row, "description") ?? "";
                var phone = NormalizeIndiaPhone(ExtractPhone(description));
                if (phone is null) continue;
                results.Add(new LeadSearchResultDto(Get(row, "url") ?? Guid.NewGuid().ToString("N"), title, null, phone, description,
                    "Brave Web Search", Get(row, "url"), .55m, true));
                if (results.Count >= req.LeadCount) break;
            }
        }
        return new LeadSearchResponse("BraveSearch", query, results,
            results.Count == 0 ? "Web results did not contain public phone numbers. Try a broader search." : null);
    }

    private async Task<LeadSearchResponse> SearchOpenStreetMapAsync(string query, LeadSearchRequest req, CancellationToken ct)
    {
        var client = _http.CreateClient("lead-discovery");
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinMasterLeadDiscovery/1.0 contact-admin");
        // Photon is a free geocoder with a less restrictive public endpoint than
        // Nominatim. Nominatim remains a fallback for compatibility.
        string? lat = null; string? lon = null;
        try
        {
            var photonUrl = $"https://photon.komoot.io/api/?q={Uri.EscapeDataString(req.Pincode + ", India")}&limit=1";
            var photon = await GetJsonWithRetryAsync<JsonElement>(client, photonUrl, ct);
            if (photon.ValueKind == JsonValueKind.Object && photon.TryGetProperty("features", out var features) && features.GetArrayLength() > 0)
            {
                var coords = features[0].GetProperty("geometry").GetProperty("coordinates");
                lon = coords[0].ToString(); lat = coords[1].ToString();
            }
        }
        catch (BusinessException) { /* use Nominatim fallback */ }
        if (lat is null || lon is null)
        {
            var geoUrl = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=in&q={Uri.EscapeDataString(req.Pincode + ", India")}";
            var geo = await GetJsonWithRetryAsync<JsonElement[]>(client, geoUrl, ct) ?? Array.Empty<JsonElement>();
            if (geo.Length == 0) return new LeadSearchResponse("OpenStreetMap", query, new(), "PIN code location was not found.");
            lat = Get(geo[0], "lat"); lon = Get(geo[0], "lon");
        }
        if (lat is null || lon is null) return new LeadSearchResponse("OpenStreetMap", query, new(), "PIN code location was not found.");
        var overpass = $"[out:json][timeout:20];(nwr[\"name\"][\"phone\"](around:8000,{lat},{lon});nwr[\"name\"][\"contact:phone\"](around:8000,{lat},{lon}););out center tags;";
        using var content = new StringContent(overpass, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await PostOverpassWithMirrorsAsync(client, content, ct);
        if (response is null || !response.IsSuccessStatusCode)
            return await SearchNominatimListingsAsync(query, req, client, ct);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var results = new List<LeadSearchResultDto>();
        if (data.TryGetProperty("elements", out var elements))
        {
            foreach (var e in elements.EnumerateArray())
            {
                if (!e.TryGetProperty("tags", out var t)) continue;
                var name = Get(t, "name");
                var phone = NormalizeIndiaPhone(Get(t, "phone") ?? Get(t, "contact:phone"));
                if (string.IsNullOrWhiteSpace(name) || phone is null) continue;
                var address = Get(t, "addr:street") ?? Get(t, "addr:full");
                results.Add(new LeadSearchResultDto(
                    Get(e, "id") ?? Guid.NewGuid().ToString("N"), name, null, phone, address,
                    "OpenStreetMap", "https://www.openstreetmap.org/", .65m, true));
                if (results.Count >= req.LeadCount) break;
            }
        }
        return new LeadSearchResponse("OpenStreetMap", query, results,
            results.Count == 0 ? "No public phone numbers were found. Try a broader business type or PIN code." : null);
    }

    /// <summary>Fast no-key fallback. Nominatim search can return OSM business objects
    /// with public phone tags without requiring an Overpass query.</summary>
    private async Task<LeadSearchResponse> SearchNominatimListingsAsync(string query, LeadSearchRequest req, HttpClient client, CancellationToken ct)
    {
        var url = $"https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&extratags=1&namedetails=1&limit={Math.Min(req.LeadCount * 3, 50)}&countrycodes=in&q={Uri.EscapeDataString(query)}";
        try
        {
            var rows = await GetJsonWithRetryAsync<JsonElement[]>(client, url, ct) ?? Array.Empty<JsonElement>();
            var results = new List<LeadSearchResultDto>();
            foreach (var row in rows)
            {
                var tags = row.TryGetProperty("extratags", out var extra) ? extra : default;
                var phone = NormalizeIndiaPhone(
                    Get(tags, "phone") ?? Get(tags, "contact:phone") ?? Get(row, "phone"));
                if (phone is null) continue;
                var name = Get(row, "name") ?? Get(row, "display_name") ?? "Business listing";
                results.Add(new LeadSearchResultDto(Get(row, "osm_id") ?? Guid.NewGuid().ToString("N"), name, null, phone,
                    Get(row, "display_name"), "OpenStreetMap Nominatim", "https://www.openstreetmap.org/", .6m, true));
                if (results.Count >= req.LeadCount) break;
            }
            return new LeadSearchResponse("OpenStreetMap Nominatim", query, results,
                results.Count == 0 ? "No public phone numbers were found for this search." : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or BusinessException or OperationCanceledException)
        {
            return new LeadSearchResponse("OpenStreetMap", query, new(), "Free public search is temporarily unavailable. Try again shortly or use a broader search.");
        }
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
        var response = await GetJsonWithRetryAsync<JsonElement>(client, url, ct);
        if (response.ValueKind == JsonValueKind.Undefined) return null;
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

    private static string? Get(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static string? ExtractPhone(string text)
    {
        var m = Regex.Match(text, @"(?:\+?91[\s-]?)?[6-9]\d{9}");
        return m.Success ? m.Value : null;
    }

    private static async Task<HttpResponseMessage?> SendWithRetryAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Recreate the request because HttpRequestMessage cannot be sent twice.
            using var current = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) current.Headers.TryAddWithoutValidation(header.Key, header.Value);
            var response = await client.SendAsync(current, ct);
            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests) return response;
            response.Dispose();
            if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(1 + attempt * 2), ct);
        }
        return null;
    }

    private static async Task<T?> GetJsonWithRetryAsync<T>(HttpClient client, string url, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await client.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                if (attempt == 2) throw new BusinessException("The public listing provider is rate-limiting requests. Please wait a minute and try again.");
                await Task.Delay(TimeSpan.FromSeconds(1 + attempt * 2), ct);
                continue;
            }
            if (!response.IsSuccessStatusCode) throw new BusinessException($"Lead provider returned {(int)response.StatusCode}.");
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
        return default;
    }

    private static async Task<HttpResponseMessage?> PostWithRetryAsync(HttpClient client, string url, HttpContent content, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(url, content, ct);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(1 + attempt * 2), ct);
                continue;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests) return response;
            response.Dispose();
            if (attempt == 2) return null;
            await Task.Delay(TimeSpan.FromSeconds(1 + attempt * 2), ct);
        }
        return null;
    }

    private static async Task<HttpResponseMessage?> PostOverpassWithMirrorsAsync(HttpClient client, HttpContent content, CancellationToken ct)
    {
        var mirrors = new[]
        {
            "https://overpass-api.de/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter",
            "https://overpass.private.coffee/api/interpreter"
        };
        foreach (var mirror in mirrors)
        {
            var response = await PostWithRetryAsync(client, mirror, content, ct);
            if (response is not null && response.IsSuccessStatusCode) return response;
            response?.Dispose();
        }
        return null;
    }
}
