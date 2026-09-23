using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;

namespace VideoAutomation.Infrastructure.AI;

/// <summary>
/// Low-level client for the Google Gemini REST API (generativelanguage.googleapis.com).
/// Handles authentication, request formatting, response parsing, and error handling.
/// Higher-level services (content generation, trend analysis) build on top of this.
/// </summary>
public class GeminiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly GeminiKeyPoolManager _poolManager;
    private readonly IUserSecrets _secrets;
    private readonly UsageTracker _usage;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        GeminiKeyPoolManager poolManager,
        IUserSecrets secrets,
        UsageTracker usage,
        ILogger<GeminiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _poolManager = poolManager;
        _secrets = secrets;
        _usage = usage;
        _logger = logger;
    }

    // The current user's Gemini key pool (own keys + own cooldown state).
    private GeminiKeyPool KeyPool => _poolManager.GetPool(_secrets.UserId ?? 0, _secrets.GeminiApiKeys);

    /// <summary>
    /// Sends a text prompt to Gemini and returns the generated text response.
    /// </summary>
    /// <param name="prompt">The user prompt to send</param>
    /// <param name="systemInstruction">Optional system instruction to guide the model</param>
    /// <param name="temperature">Creativity level (0.0 = deterministic, 1.0 = creative)</param>
    /// <param name="maxOutputTokens">Maximum tokens in the response</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The generated text content</returns>
    public async Task<string> GenerateContentAsync(
        string prompt,
        string? systemInstruction = null,
        double temperature = 0.7,
        int maxOutputTokens = 4096,
        CancellationToken ct = default)
    {
        // Build request body (key resolution + validation happens in ExecuteAsync via the per-user pool).
        var requestBody = BuildRequestBody(prompt, systemInstruction, temperature, maxOutputTokens);
        return await ExecuteAsync(requestBody, $"prompt length={prompt.Length}", ct);
    }

    /// <summary>
    /// Multi-turn chat: sends the whole conversation (mapped to Gemini's user/model roles) so the model
    /// keeps context across turns, instead of flattening it into one prompt. Powers the manual "chat with
    /// Gemini to shape a video idea" page — the trend pipeline still uses the single-prompt GenerateContentAsync.
    /// </summary>
    /// <param name="turns">Conversation so far, oldest first. Role must be "user" or "assistant"/"model".</param>
    public async Task<string> GenerateChatAsync(
        IReadOnlyList<(string Role, string Text)> turns,
        string? systemInstruction = null,
        double temperature = 0.8,
        int maxOutputTokens = 2048,
        CancellationToken ct = default)
    {
        if (turns.Count == 0)
            throw new InvalidOperationException("Sohbet geçmişi boş — en az bir mesaj gerekli.");

        var requestBody = BuildChatRequestBody(turns, systemInstruction, temperature, maxOutputTokens);
        return await ExecuteAsync(requestBody, $"chat turns={turns.Count}", ct);
    }

    /// <summary>
    /// Multimodal: has Gemini actually WATCH a YouTube video (passed by URL, no download) and answer the
    /// prompt about it. This is how the pipeline understands a short-form video's real content — most trends
    /// have no transcript, so a title-only guess is worthless; the model seeing the actual visuals+audio is
    /// the fix. YouTube URLs are accepted directly as a fileData part; TikTok/Instagram would need the video
    /// uploaded via the Files API instead (not implemented here yet).
    /// </summary>
    public async Task<string> GenerateFromYouTubeAsync(
        string youtubeUrl,
        string prompt,
        string? systemInstruction = null,
        double temperature = 0.4,
        int maxOutputTokens = 2048,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(youtubeUrl))
            throw new InvalidOperationException("YouTube URL boş — video analiz edilemez.");

        var requestBody = BuildYouTubeRequestBody(youtubeUrl, prompt, systemInstruction, temperature, maxOutputTokens);
        return await ExecuteAsync(requestBody, $"youtube video {youtubeUrl}", ct);
    }

    /// <summary>
    /// Shared transport: POSTs a pre-built request body to the model, handles transport errors, non-success
    /// status, text extraction and the "blocked by safety filter" special case — everything except how the
    /// body was shaped. Rotates through the GeminiKeyPool: if a key returns 429 (its quota) it's cooled down
    /// and the next key is tried, so one exhausted key doesn't fail the request while others have budget.
    /// </summary>
    private async Task<string> ExecuteAsync(string requestBody, string logLabel, CancellationToken ct)
    {
        var pool = KeyPool;
        var keys = pool.KeysToTry();
        if (keys.Count == 0)
            throw new InvalidOperationException("Gemini API anahtarı yapılandırılmadı — panelden (🔑 API Anahtarları) Gemini anahtarını gir.");

        var model = _configuration["AI:GeminiModel"] ?? "gemini-2.0-flash";
        var client = _httpClientFactory.CreateClient("Gemini");

        _logger.LogDebug("🤖 Gemini request: model={Model}, {Label}, anahtar sayısı={KeyCount}", model, logLabel, keys.Count);

        InvalidOperationException? lastKeyError = null;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var url = $"v1beta/models/{model}:generateContent?key={key}";

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(
                    url,
                    new StringContent(requestBody, Encoding.UTF8, "application/json"),
                    ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "Gemini API request failed");
                throw new InvalidOperationException($"Gemini API request failed: {ex.Message}", ex);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);

            // Key-specific failures → cool this key down and fail over to the next one (the whole point of
            // the pool). 429 = quota/rate (recovers soon, short cooldown). 403/401 = the project/key was
            // denied or is invalid (Google-side, usually permanent) → long cooldown so it drops out of
            // rotation but the pool keeps working on the remaining keys.
            var status = response.StatusCode;
            if (status is System.Net.HttpStatusCode.TooManyRequests
                       or System.Net.HttpStatusCode.Forbidden
                       or System.Net.HttpStatusCode.Unauthorized)
            {
                var is429 = status == System.Net.HttpStatusCode.TooManyRequests;
                var cooldown = is429 ? (ParseRetryDelay(responseJson) ?? TimeSpan.FromSeconds(60)) : TimeSpan.FromHours(6);
                pool.ReportQuotaExhausted(key, cooldown);
                _logger.LogWarning("Gemini anahtar #{Index}/{Total} {Status} ({Reason}) — {Cooldown:N0}sn devre dışı, sıradaki anahtara geçiliyor",
                    i + 1, keys.Count, (int)status, is429 ? "kota" : "reddedildi/geçersiz", cooldown.TotalSeconds);
                lastKeyError = new InvalidOperationException(is429
                    ? $"Tüm Gemini anahtarları ({keys.Count}) kota/hız sınırına takıldı (429). Biraz sonra tekrar deneyin ya da yeni anahtar ekleyin."
                    : $"Kullanılabilir Gemini anahtarı kalmadı: en az bir proje reddedilmiş/geçersiz (403/401). Reddedilen anahtarı .env'den kaldırıp geçerli biriyle değiştirin.");
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Not key-specific (400 bad request, 5xx server error) — another key wouldn't help; surface it.
                _logger.LogError("Gemini API error {StatusCode}: {Response}", (int)status, responseJson);
                throw new InvalidOperationException(
                    $"Gemini API returned {(int)status}: {ExtractErrorMessage(responseJson)}");
            }

            var text = ExtractTextFromResponse(responseJson);

            if (string.IsNullOrEmpty(text))
            {
                // Empty text usually means Gemini's safety filters blocked the prompt/response rather than a
                // real API error — surface that distinctly (and don't rotate keys; another key won't help).
                var blockReason = ExtractBlockReason(responseJson);
                if (blockReason != null)
                    throw new InvalidOperationException($"Gemini yanıtı engellendi ({blockReason}).");
            }

            if (i > 0)
                _logger.LogInformation("Gemini isteği #{Index}. anahtarla başarılı oldu (öncekiler kota/red nedeniyle atlandı)", i + 1);

            _logger.LogDebug("✅ Gemini response: {Length} chars", text.Length);
            await _usage.TrackAsync("Gemini", ct);  // best-effort per-user/day counter (free tier has no usage API)
            return text;
        }

        // Every configured key hit a key-specific failure (429/403/401).
        throw lastKeyError ?? new InvalidOperationException("Kullanılabilir Gemini anahtarı kalmadı.");
    }

    /// <summary>Parses Gemini's 429 "retryDelay" (e.g. "37s") into a cooldown; null if absent/unparseable.</summary>
    private static TimeSpan? ParseRetryDelay(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("details", out var details)
                && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (d.TryGetProperty("retryDelay", out var rd) && rd.ValueKind == JsonValueKind.String)
                    {
                        var s = rd.GetString();
                        if (!string.IsNullOrEmpty(s) && s.EndsWith('s') && double.TryParse(s[..^1], out var secs))
                            return TimeSpan.FromSeconds(Math.Clamp(secs, 1, 3600));
                    }
                }
            }
        }
        catch { /* best-effort */ }
        return null;
    }

    private static string? ExtractBlockReason(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("promptFeedback", out var feedback)
                && feedback.TryGetProperty("blockReason", out var reason))
                return reason.GetString();

            if (doc.RootElement.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0
                && candidates[0].TryGetProperty("finishReason", out var finish))
                return finish.GetString();
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Sends a prompt expecting a JSON response. Strips markdown code fences if present.
    /// </summary>
    public async Task<T?> GenerateJsonAsync<T>(
        string prompt,
        string? systemInstruction = null,
        double temperature = 0.4,
        int maxOutputTokens = 4096,
        CancellationToken ct = default) where T : class
    {
        var raw = await GenerateContentAsync(prompt, systemInstruction, temperature, maxOutputTokens, ct);

        // Strip markdown code fences (```json ... ```)
        var cleaned = CleanJsonResponse(raw);

        try
        {
            return JsonSerializer.Deserialize<T>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Gemini JSON response. Raw: {Raw}", raw);
            throw new InvalidOperationException($"Gemini returned invalid JSON: {ex.Message}", ex);
        }
    }

    // ── Private helpers ─────────────────────────────────────────────

    private static string BuildRequestBody(
        string prompt, string? systemInstruction, double temperature, int maxOutputTokens)
    {
        var body = new Dictionary<string, object>
        {
            ["contents"] = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt } }
                }
            },
            ["generationConfig"] = new
            {
                temperature,
                maxOutputTokens,
                topP = 0.95,
                topK = 40
            }
        };

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            body["systemInstruction"] = new
            {
                parts = new[] { new { text = systemInstruction } }
            };
        }

        return JsonSerializer.Serialize(body);
    }

    private static string BuildChatRequestBody(
        IReadOnlyList<(string Role, string Text)> turns, string? systemInstruction,
        double temperature, int maxOutputTokens)
    {
        // Gemini only accepts "user" and "model" roles; our chat stores "assistant" for the model's turns.
        var contents = turns.Select(t => new
        {
            role = t.Role is "assistant" or "model" ? "model" : "user",
            parts = new[] { new { text = t.Text } }
        }).ToArray();

        var body = new Dictionary<string, object>
        {
            ["contents"] = contents,
            ["generationConfig"] = new { temperature, maxOutputTokens, topP = 0.95, topK = 40 }
        };

        if (!string.IsNullOrWhiteSpace(systemInstruction))
            body["systemInstruction"] = new { parts = new[] { new { text = systemInstruction } } };

        return JsonSerializer.Serialize(body);
    }

    private static string BuildYouTubeRequestBody(
        string youtubeUrl, string prompt, string? systemInstruction, double temperature, int maxOutputTokens)
    {
        // A single content turn with two parts: the video (fileData/fileUri — Gemini fetches & watches it)
        // and the text question. object[] because the two parts are different anonymous shapes.
        var body = new Dictionary<string, object>
        {
            ["contents"] = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { fileData = new { fileUri = youtubeUrl } },
                        new { text = prompt },
                    }
                }
            },
            ["generationConfig"] = new { temperature, maxOutputTokens, topP = 0.95, topK = 40 }
        };

        if (!string.IsNullOrWhiteSpace(systemInstruction))
            body["systemInstruction"] = new { parts = new[] { new { text = systemInstruction } } };

        return JsonSerializer.Serialize(body);
    }

    private static string ExtractTextFromResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0)
        {
            var firstCandidate = candidates[0];
            if (firstCandidate.TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array
                && parts.GetArrayLength() > 0)
            {
                var firstPart = parts[0];
                if (firstPart.TryGetProperty("text", out var textEl))
                {
                    return textEl.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static string ExtractErrorMessage(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var msg))
            {
                return msg.GetString() ?? "Unknown error";
            }
        }
        catch { }

        return responseJson.Length > 200 ? responseJson[..200] : responseJson;
    }

    /// <summary>
    /// Strips ```json and ``` fences that Gemini sometimes wraps around JSON output.
    /// Internal (not private) so callers that need the raw response text alongside the parsed
    /// result (e.g. to persist RawJsonResponse) can reuse this instead of calling GenerateContentAsync twice.
    /// </summary>
    internal static string CleanJsonResponse(string raw)
    {
        var trimmed = raw.Trim();

        // Remove ```json ... ``` or ``` ... ```
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];

            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3];
        }

        return trimmed.Trim();
    }
}
