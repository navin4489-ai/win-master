using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WinMaster.Core;
using WinMaster.Core.Interfaces;

namespace WinMaster.Infrastructure.Services;

/// <summary>
/// Chat-completion client supporting several FREE providers plus a keyless local generator.
///
/// Free options (all have no-cost tiers):
///   • Groq        — OpenAI-compatible, very fast (llama-3.3-70b, llama-3.1-8b-instant, mixtral)
///   • OpenRouter  — OpenAI-compatible, many ":free" models (llama-3.2, qwen, gemma)
///   • Gemini      — Google AI Studio free tier (gemini-1.5-flash)
///   • Local       — built-in template generator, NO API KEY required (default fallback)
/// OpenAI / Anthropic remain for users who bring a paid key.
/// </summary>
public class AiClient : IAiClient
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<AiClient> _log;
    public AiClient(IHttpClientFactory http, ILogger<AiClient> log) { _http = http; _log = log; }

    public async Task<AiReply> CompleteAsync(AiProvider provider, string? apiKey, string model,
        IReadOnlyList<AiMessage> messages, CancellationToken ct = default)
    {
        try
        {
            return provider switch
            {
                AiProvider.Local => LocalReply(model, messages),
                AiProvider.Gemini => await GeminiAsync(apiKey, model, messages, ct),
                _ => await OpenAiCompatibleAsync(provider, apiKey, model, messages, ct),
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AI provider {Provider} failed; using local generator.", provider);
            return LocalReply(model, messages);
        }
    }

    // ---- OpenAI-compatible (OpenAI, Groq, OpenRouter) ----
    private async Task<AiReply> OpenAiCompatibleAsync(AiProvider provider, string? apiKey, string model,
        IReadOnlyList<AiMessage> messages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return LocalReply(model, messages);
        var baseUrl = provider switch
        {
            AiProvider.Groq => "https://api.groq.com/openai/v1",
            AiProvider.OpenRouter => "https://openrouter.ai/api/v1",
            _ => "https://api.openai.com/v1",
        };
        var client = _http.CreateClient("ai");
        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
        if (provider == AiProvider.OpenRouter)
        {
            req.Headers.Add("HTTP-Referer", "https://winmaster.app");
            req.Headers.Add("X-Title", "Win Master");
        }
        req.Content = JsonContent.Create(new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            temperature = 0.7,
            max_tokens = 400,
        });
        var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("AI {Provider} HTTP {Code}", provider, (int)resp.StatusCode);
            return LocalReply(model, messages);
        }
        var text = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        var tokens = json.TryGetProperty("usage", out var u) && u.TryGetProperty("total_tokens", out var t) ? t.GetInt32() : text.Length / 4;
        return new AiReply(text.Trim(), tokens, model);
    }

    // ---- Google Gemini ----
    private async Task<AiReply> GeminiAsync(string? apiKey, string model, IReadOnlyList<AiMessage> messages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return LocalReply(model, messages);
        var client = _http.CreateClient("ai");
        var sys = messages.FirstOrDefault(m => m.Role == "system")?.Content;
        var contents = messages.Where(m => m.Role != "system")
            .Select(m => new { role = m.Role == "assistant" ? "model" : "user", parts = new[] { new { text = m.Content } } });
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
        var body = new Dictionary<string, object?>
        {
            ["contents"] = contents,
            ["generationConfig"] = new { temperature = 0.7, maxOutputTokens = 400 },
        };
        if (!string.IsNullOrEmpty(sys)) body["systemInstruction"] = new { parts = new[] { new { text = sys } } };
        var resp = await client.PostAsJsonAsync(url, body, ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!resp.IsSuccessStatusCode) return LocalReply(model, messages);
        var text = json.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        return new AiReply(text.Trim(), text.Length / 4, model);
    }

    // ---- Keyless local generator ----
    // Produces useful marketing copy / replies with no external call, so AI features
    // work out of the box before any free key is configured.
    private static AiReply LocalReply(string model, IReadOnlyList<AiMessage> messages)
    {
        var user = messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        var sys = messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
        var text = LocalGenerator.Respond(user, sys);
        return new AiReply(text, text.Length / 4, "local-generator");
    }
}

/// <summary>Lightweight rule/template based generator used as a no-key fallback.</summary>
public static class LocalGenerator
{
    public static string Respond(string input, string systemPrompt)
    {
        var lower = input.ToLowerInvariant();

        // If asked to generate marketing copy, synthesize a campaign-ready message.
        if (lower.Contains("write") || lower.Contains("generate") || lower.Contains("draft") ||
            lower.Contains("promo") || lower.Contains("offer") || lower.Contains("campaign") || lower.Contains("message"))
        {
            return GenerateMarketing(input);
        }

        // Otherwise behave like a friendly support assistant.
        if (lower.Contains("price") || lower.Contains("cost") || lower.Contains("how much"))
            return "Great question! Our plans start affordably and scale with your needs. Would you like me to share the full pricing or set up a quick call?";
        if (lower.Contains("hi") || lower.Contains("hello") || lower.Contains("hey"))
            return "Hello! 👋 Thanks for reaching out. How can we help you today?";
        if (lower.Contains("stop") || lower.Contains("unsubscribe"))
            return "No problem — you've been unsubscribed and won't receive further messages. Reply START anytime to opt back in.";
        if (lower.Contains("thank"))
            return "You're very welcome! 😊 Let us know if there's anything else we can do.";
        if (lower.Contains("help") || lower.Contains("support") || lower.Contains("problem"))
            return "I'm here to help! Could you share a little more detail about what you need, and I'll get it sorted right away.";

        return "Thanks for your message! A member of our team will follow up shortly. In the meantime, how else can I assist you?";
    }

    public static string GenerateMarketing(string prompt)
    {
        var topic = prompt;
        foreach (var w in new[] { "write", "generate", "draft", "a", "an", "message", "about", "for", "promo", "promotion", "offer", "campaign", "me", "please" })
            topic = System.Text.RegularExpressions.Regex.Replace(topic, $@"\b{w}\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        topic = topic.Trim(' ', ':', '-', '.', ',');
        if (string.IsNullOrWhiteSpace(topic)) topic = "our latest offer";

        var sb = new StringBuilder();
        sb.Append("{Hi|Hello|Hey} {{Name}}! 👋\n\n");
        sb.Append($"{{Great news|Exciting update|Just for you}} — {topic} is here! ");
        sb.Append("{Don't miss out|Limited time only|Grab it while you can}.\n\n");
        sb.Append("✅ Exclusive deal for our WhatsApp community\n");
        sb.Append("✅ Fast, friendly service\n\n");
        sb.Append("{Reply YES|Tap the button|Message us} to claim yours. 🚀");
        return sb.ToString();
    }
}
