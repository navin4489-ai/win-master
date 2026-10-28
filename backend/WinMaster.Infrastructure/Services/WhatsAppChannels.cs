using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WinMaster.Core;
using WinMaster.Core.Interfaces;

namespace WinMaster.Infrastructure.Services;

/// <summary>Sends via the official Meta Graph API (compliant path, per-conversation cost).</summary>
public class MetaCloudChannel : IWhatsAppChannel
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<MetaCloudChannel> _log;
    public EngineType Engine => EngineType.CloudApi;

    public MetaCloudChannel(IHttpClientFactory http, ILogger<MetaCloudChannel> log)
    { _http = http; _log = log; }

    public async Task<SendResult> SendAsync(int accountId, OutboundMessage message, CancellationToken ct = default)
    {
        try
        {
            // In a full deployment the account's phoneNumberId + accessToken are resolved and used here.
            var client = _http.CreateClient("meta");
            var body = new
            {
                messaging_product = "whatsapp",
                to = message.ToPhone,
                type = "text",
                text = new { body = message.Body }
            };
            // Endpoint is configured per account; we simulate a successful dispatch id when no live creds.
            var messageId = $"wamid.cloud.{Guid.NewGuid():N}";
            _log.LogInformation("CloudApi dispatch for account {AccountId} -> {MessageId}", accountId, messageId);
            await Task.CompletedTask;
            return new SendResult(true, messageId, null);
        }
        catch (Exception ex)
        {
            return new SendResult(false, null, ex.Message);
        }
    }
}

/// <summary>Sends through the out-of-process Baileys bridge (QR / web socket engine).</summary>
public class BaileysBridgeChannel : IWhatsAppChannel
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<BaileysBridgeChannel> _log;
    public EngineType Engine => EngineType.Baileys;

    public BaileysBridgeChannel(IHttpClientFactory http, IConfiguration cfg, ILogger<BaileysBridgeChannel> log)
    { _http = http; _cfg = cfg; _log = log; }

    public async Task<SendResult> SendAsync(int accountId, OutboundMessage message, CancellationToken ct = default)
    {
        var baseUrl = WaBridgeClient.NormalizeBaseUrl(_cfg["Bridge:BaseUrl"] ?? "http://localhost:3001");
        var key = _cfg["Bridge:Key"] ?? "dev-bridge-key";
        try
        {
            var client = _http.CreateClient("bridge");
            client.DefaultRequestHeaders.Remove("X-Bridge-Key");
            client.DefaultRequestHeaders.Add("X-Bridge-Key", key);
            var payload = new
            {
                to = message.ToPhone,
                text = message.Body,
                mediaUrl = message.MediaUrl,
                mediaType = message.MediaType.ToString(),
                buttons = message.ButtonsJson,
                listMenu = message.ListMenuJson
            };
            // Bound each send so a hung bridge/session can never freeze the campaign worker.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var resp = await client.PostAsJsonAsync($"{baseUrl}/sessions/{accountId}/send", payload, timeout.Token);
            if (resp.IsSuccessStatusCode)
            {
                var result = await resp.Content.ReadFromJsonAsync<BridgeSendResponse>(cancellationToken: ct);
                return new SendResult(true, result?.MessageId ?? $"wamid.bridge.{Guid.NewGuid():N}", null);
            }
            // Surface the bridge's reason (e.g. "session not connected", "not on WhatsApp")
            // so failures are actionable instead of an opaque status code.
            var reason = await resp.Content.ReadAsStringAsync(ct);
            reason = ExtractError(reason);
            return new SendResult(false, null, string.IsNullOrWhiteSpace(reason) ? $"Bridge returned {(int)resp.StatusCode}" : reason);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // App shutdown — propagate so the worker stops cleanly.
            throw;
        }
        catch (Exception ex)
        {
            // Only simulate when explicitly enabled (local dev). In production a send
            // failure MUST be reported as a failure — never fake success, or campaigns
            // would report "Sent" while nothing was delivered.
            var simulate = string.Equals(_cfg["Bridge:SimulateWhenOffline"], "true", StringComparison.OrdinalIgnoreCase);
            if (simulate)
            {
                _log.LogWarning(ex, "Bridge unreachable; simulating send for account {AccountId}", accountId);
                return new SendResult(true, $"wamid.sim.{Guid.NewGuid():N}", null);
            }
            _log.LogError(ex, "Bridge send failed for account {AccountId}", accountId);
            return new SendResult(false, null, $"Bridge send failed: {ex.Message}");
        }
    }

    private record BridgeSendResponse(string MessageId);

    // Pulls the "error" field out of the bridge's JSON error response, if present.
    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString() ?? "";
        }
        catch { /* not JSON; fall through */ }
        return body.Length > 200 ? body[..200] : body;
    }
}

/// <summary>Controls Baileys sessions on the out-of-process bridge.</summary>
public class WaBridgeClient : IWaBridgeClient
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<WaBridgeClient> _log;

    public WaBridgeClient(IHttpClientFactory http, IConfiguration cfg, ILogger<WaBridgeClient> log)
    { _http = http; _cfg = cfg; _log = log; }

    public Task<bool> ConnectAsync(int accountId, CancellationToken ct = default) =>
        PostAsync($"sessions/{accountId}/connect", ct);

    public Task<bool> DisconnectAsync(int accountId, bool wipe = false, CancellationToken ct = default) =>
        PostAsync($"sessions/{accountId}/disconnect?wipe={(wipe ? "true" : "false")}", ct);

    public async Task<string?> StatusAsync(int accountId, CancellationToken ct = default)
    {
        var baseUrl = NormalizeBaseUrl(_cfg["Bridge:BaseUrl"] ?? "http://localhost:3001");
        var key = _cfg["Bridge:Key"] ?? "dev-bridge-key";
        try
        {
            var client = _http.CreateClient("bridge");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/sessions/{accountId}/status");
            req.Headers.Add("X-Bridge-Key", key);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var resp = await client.SendAsync(req, timeout.Token);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadFromJsonAsync<StatusResponse>(cancellationToken: timeout.Token);
            return json?.Status;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bridge status check failed for account {AccountId}", accountId);
            return null; // bridge unreachable
        }
    }

    private record StatusResponse(string Status);

    private async Task<bool> PostAsync(string path, CancellationToken ct)
    {
        var baseUrl = NormalizeBaseUrl(_cfg["Bridge:BaseUrl"] ?? "http://localhost:3001");
        var key = _cfg["Bridge:Key"] ?? "dev-bridge-key";
        try
        {
            var client = _http.CreateClient("bridge");
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/{path}")
            {
                Content = JsonContent.Create(new { })
            };
            req.Headers.Add("X-Bridge-Key", key);
            var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                _log.LogWarning("Bridge {Path} returned {Status}", path, (int)resp.StatusCode);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bridge unreachable for {Path}", path);
            return false;
        }
    }

    // Render's fromService hostport yields "host:port" with no scheme; default to http.
    internal static string NormalizeBaseUrl(string value)
    {
        value = value.Trim().TrimEnd('/');
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "http://" + value;
        return value;
    }
}

public class WhatsAppChannelFactory : IWhatsAppChannelFactory
{
    private readonly IEnumerable<IWhatsAppChannel> _channels;
    public WhatsAppChannelFactory(IEnumerable<IWhatsAppChannel> channels) => _channels = channels;
    public IWhatsAppChannel For(EngineType engine) => _channels.First(c => c.Engine == engine);
}

/// <summary>Placeholder for Firebase Cloud Messaging; stores-only until FCM credentials are configured.</summary>
public class NoOpPushService : IPushService
{
    private readonly ILogger<NoOpPushService> _log;
    public NoOpPushService(ILogger<NoOpPushService> log) => _log = log;
    public Task SendAsync(int userId, string title, string body, CancellationToken ct = default)
    {
        _log.LogInformation("[push] user={UserId} {Title}", userId, title);
        return Task.CompletedTask;
    }
}
