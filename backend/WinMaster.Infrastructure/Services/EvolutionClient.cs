using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WinMaster.Core;
using WinMaster.Core.Interfaces;

namespace WinMaster.Infrastructure.Services;

/// <summary>
/// Talks to a self-hosted Evolution API server (https://github.com/EvolutionAPI/evolution-api)
/// for the Evolution engine. Evolution wraps Baileys (and optionally the official Cloud API)
/// behind a stable REST API with multi-instance support and robust session persistence.
///
/// One Win Master account == one Evolution "instance" named "wm-{accountId}". This class both
/// SENDS messages (IWhatsAppChannel) and manages the session lifecycle (connect/status/qr),
/// exposed to the rest of the app through IEvolutionClient.
///
/// Config:
///   Evolution:BaseUrl  (e.g. http://evolution:8080)
///   Evolution:ApiKey   (the server's AUTHENTICATION_API_KEY / global apikey)
/// </summary>
public interface IEvolutionClient
{
    bool IsConfigured { get; }
    Task<bool> ConnectAsync(int accountId, CancellationToken ct = default);
    Task<bool> DisconnectAsync(int accountId, bool wipe = false, CancellationToken ct = default);
    /// <summary>"connected" | "connecting" | "qr" | "disconnected" | null (server unreachable).</summary>
    Task<string?> StatusAsync(int accountId, CancellationToken ct = default);
    /// <summary>Returns the pending QR (base64 PNG without the data: prefix) or null.</summary>
    Task<string?> QrBase64Async(int accountId, CancellationToken ct = default);
}

public class EvolutionClient : IEvolutionClient, IWhatsAppChannel
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<EvolutionClient> _log;

    public EvolutionClient(IHttpClientFactory http, IConfiguration cfg, ILogger<EvolutionClient> log)
    { _http = http; _cfg = cfg; _log = log; }

    public EngineType Engine => EngineType.Evolution;

    private string BaseUrl => (_cfg["Evolution:BaseUrl"] ?? "").TrimEnd('/');
    private string ApiKey => _cfg["Evolution:ApiKey"] ?? "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    private static string Instance(int accountId) => $"wm-{accountId}";

    private HttpClient Client()
    {
        var c = _http.CreateClient("evolution");
        c.DefaultRequestHeaders.Remove("apikey");
        c.DefaultRequestHeaders.Add("apikey", ApiKey);
        c.Timeout = TimeSpan.FromSeconds(30);
        return c;
    }

    // ---- Lifecycle ----

    /// <summary>Creates the instance if needed, then starts the QR/connect flow.</summary>
    public async Task<bool> ConnectAsync(int accountId, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        var name = Instance(accountId);
        try
        {
            var client = Client();
            // Create the instance (idempotent — if it already exists Evolution returns 403/409 which we ignore).
            var createBody = new
            {
                instanceName = name,
                qrcode = true,
                integration = "WHATSAPP-BAILEYS"
            };
            using (var createResp = await client.PostAsJsonAsync($"{BaseUrl}/instance/create", createBody, ct))
            {
                // 2xx = created; 403/409/400 typically "already in use" — both fine, we then connect.
                if (!createResp.IsSuccessStatusCode)
                    _log.LogInformation("Evolution create instance {Name} -> {Status} (continuing to connect)", name, (int)createResp.StatusCode);
            }
            // Start the connect/QR flow.
            using var connResp = await client.GetAsync($"{BaseUrl}/instance/connect/{name}", ct);
            return connResp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Evolution connect failed for account {AccountId}", accountId);
            return false;
        }
    }

    public async Task<bool> DisconnectAsync(int accountId, bool wipe = false, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        var name = Instance(accountId);
        try
        {
            var client = Client();
            using (await client.DeleteAsync($"{BaseUrl}/instance/logout/{name}", ct)) { }
            if (wipe)
                using (await client.DeleteAsync($"{BaseUrl}/instance/delete/{name}", ct)) { }
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Evolution disconnect failed for account {AccountId}", accountId);
            return false;
        }
    }

    public async Task<string?> StatusAsync(int accountId, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;
        var name = Instance(accountId);
        try
        {
            var client = Client();
            using var resp = await client.GetAsync($"{BaseUrl}/instance/connectionState/{name}", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return "disconnected";
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            var state = ExtractState(json);
            return state switch
            {
                "open" => "connected",
                "connecting" => "connecting",
                "close" => "disconnected",
                _ => "disconnected"
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Evolution status failed for account {AccountId}", accountId);
            return null;
        }
    }

    public async Task<string?> QrBase64Async(int accountId, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;
        var name = Instance(accountId);
        try
        {
            var client = Client();
            using var resp = await client.GetAsync($"{BaseUrl}/instance/connect/{name}", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            return ExtractQrBase64(json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Evolution QR fetch failed for account {AccountId}", accountId);
            return null;
        }
    }

    // ---- Sending ----

    public async Task<SendResult> SendAsync(int accountId, OutboundMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured) return new SendResult(false, null, "Evolution API is not configured on the server.");
        var name = Instance(accountId);
        var number = new string((message.ToPhone ?? "").Where(char.IsDigit).ToArray());
        try
        {
            var client = Client();
            HttpResponseMessage resp;
            if (message.MediaType != MediaType.None && !string.IsNullOrWhiteSpace(message.MediaUrl))
            {
                var mediatype = message.MediaType switch
                {
                    MediaType.Image => "image",
                    MediaType.Video => "video",
                    MediaType.Audio => "audio",
                    _ => "document"
                };
                var mediaBody = new { number, mediatype, media = message.MediaUrl, caption = message.Body };
                resp = await client.PostAsJsonAsync($"{BaseUrl}/message/sendMedia/{name}", mediaBody, ct);
            }
            else
            {
                var textBody = new { number, text = message.Body };
                resp = await client.PostAsJsonAsync($"{BaseUrl}/message/sendText/{name}", textBody, ct);
            }

            var respJson = await resp.Content.ReadAsStringAsync(ct);
            resp.Dispose();
            if (!resp.IsSuccessStatusCode)
            {
                var err = ExtractError(respJson) ?? $"Evolution returned {(int)resp.StatusCode}";
                // A "not found"/"does not exist" instance error means the session isn't live.
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound
                    || err.Contains("not connected", StringComparison.OrdinalIgnoreCase)
                    || err.Contains("not exist", StringComparison.OrdinalIgnoreCase)
                    || err.Contains("Connection Closed", StringComparison.OrdinalIgnoreCase))
                    return new SendResult(false, null, "WhatsApp session not connected. Scan the QR under Accounts to reconnect.");
                return new SendResult(false, null, err);
            }
            var id = ExtractMessageId(respJson) ?? $"evo.{Guid.NewGuid():N}";
            return new SendResult(true, id, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Evolution send failed for account {AccountId}", accountId);
            return new SendResult(false, null, $"Evolution send failed: {ex.Message}");
        }
    }

    // ---- JSON helpers ----

    private static string? ExtractState(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("instance", out var inst) && inst.TryGetProperty("state", out var s1)) return s1.GetString();
            if (root.TryGetProperty("state", out var s2)) return s2.GetString();
        }
        catch { }
        return null;
    }

    private static string? ExtractQrBase64(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // Evolution returns { base64: "data:image/png;base64,...." } or { qrcode: { base64 } }
            string? b64 = null;
            if (root.TryGetProperty("base64", out var b)) b64 = b.GetString();
            else if (root.TryGetProperty("qrcode", out var q) && q.TryGetProperty("base64", out var qb)) b64 = qb.GetString();
            if (string.IsNullOrWhiteSpace(b64)) return null;
            var idx = b64.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? b64[(idx + 7)..] : b64;
        }
        catch { }
        return null;
    }

    private static string? ExtractMessageId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("key", out var key) && key.TryGetProperty("id", out var id)) return id.GetString();
        }
        catch { }
        return null;
    }

    private static string? ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var m))
            {
                if (m.ValueKind == JsonValueKind.String) return m.GetString();
                if (m.ValueKind == JsonValueKind.Array && m.GetArrayLength() > 0) return m[0].GetString();
            }
            if (root.TryGetProperty("error", out var e)) return e.GetString();
            if (root.TryGetProperty("response", out var r) && r.TryGetProperty("message", out var rm))
                return rm.ValueKind == JsonValueKind.Array && rm.GetArrayLength() > 0 ? rm[0].GetString() : rm.GetString();
        }
        catch { }
        return json.Length > 200 ? json[..200] : json;
    }
}
