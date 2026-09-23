using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Media;

/// <summary>
/// Low-level client for Google Veo video generation via the Gemini API (generativelanguage.googleapis.com).
/// Uses the same Gemini API key as GeminiClient (ApiKeys:GeminiApiKey) — no separate token needed.
/// Unlike GeminiClient's synchronous generateContent calls, video generation is an async
/// Long-Running Operation: submit via predictLongRunning, then poll the returned operation name
/// until done=true. Auth is via the x-goog-api-key HEADER (not the ?key= query param GeminiClient uses).
/// </summary>
public class VeoClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<VeoClient> _logger;

    public VeoClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, IUserSecrets secrets, ILogger<VeoClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _secrets = secrets;
        _logger = logger;
    }

    public async Task<byte[]> GenerateVideoAsync(string prompt, CancellationToken ct = default)
    {
        var apiKey = _secrets.GeminiApiKeys.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new VideoGenerationException("Gemini API anahtarı yapılandırılmamış — panelden (🔑 API Anahtarları) Gemini anahtarını gir.");

        var model = _configuration.GetValue("Media:Veo:Model", "veo-3.1-generate-preview")!;
        var aspectRatio = _configuration.GetValue("Media:Veo:AspectRatio", "9:16")!;
        var durationSeconds = _configuration.GetValue("Media:Veo:DurationSeconds", 8);
        var resolution = _configuration.GetValue("Media:Veo:Resolution", "720p")!;

        var client = _httpClientFactory.CreateClient("Veo");
        client.DefaultRequestHeaders.Remove("x-goog-api-key");
        client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

        var operationName = await SubmitAsync(client, model, prompt, aspectRatio, durationSeconds, resolution, ct);
        var videoUri = await PollUntilCompleteAsync(client, operationName, ct);
        return await DownloadAsync(client, videoUri, ct);
    }

    private async Task<string> SubmitAsync(HttpClient client, string model, string prompt, string aspectRatio, int durationSeconds, string resolution, CancellationToken ct)
    {
        // durationSeconds must be a JSON number — the Gemini API rejects a quoted string here
        // despite some docs examples showing it quoted (verified via a live 400 response).
        var body = JsonSerializer.Serialize(new
        {
            instances = new[] { new { prompt } },
            parameters = new { aspectRatio, durationSeconds, resolution, personGeneration = "allow_all" }
        });

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync($"models/{model}:predictLongRunning", new StringContent(body, Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new VideoGenerationException($"Veo video isteği başarısız: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new VideoGenerationException($"Veo API {(int)response.StatusCode} döndürdü: {json[..Math.Min(json.Length, 300)]}");

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("name", out var nameEl))
            throw new VideoGenerationException($"Veo API yanıtında 'name' yok: {json[..Math.Min(json.Length, 300)]}");

        return nameEl.GetString() ?? throw new VideoGenerationException("Veo API boş operation adı döndürdü.");
    }

    private async Task<string> PollUntilCompleteAsync(HttpClient client, string operationName, CancellationToken ct)
    {
        var pollInterval = TimeSpan.FromSeconds(_configuration.GetValue("Media:Veo:PollIntervalSeconds", 10));
        var maxAttempts = _configuration.GetValue("Media:Veo:MaxPollAttempts", 40);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await Task.Delay(pollInterval, ct);

            var response = await client.GetAsync(operationName, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new VideoGenerationException($"Veo durum sorgusu {(int)response.StatusCode} döndürdü: {json[..Math.Min(json.Length, 300)]}");

            using var doc = JsonDocument.Parse(json);
            var done = doc.RootElement.TryGetProperty("done", out var doneEl) && doneEl.ValueKind == JsonValueKind.True;

            _logger.LogDebug("Veo operation {Name} done={Done} (attempt {Attempt}/{Max})", operationName, done, attempt, maxAttempts);

            if (!done) continue;

            if (doc.RootElement.TryGetProperty("error", out var errorEl))
                throw new VideoGenerationException($"Veo video üretimi başarısız: {errorEl}");

            if (doc.RootElement.TryGetProperty("response", out var responseEl)
                && responseEl.TryGetProperty("generateVideoResponse", out var genResp)
                && genResp.TryGetProperty("generatedSamples", out var samples)
                && samples.ValueKind == JsonValueKind.Array && samples.GetArrayLength() > 0
                && samples[0].TryGetProperty("video", out var videoEl)
                && videoEl.TryGetProperty("uri", out var uriEl)
                && uriEl.GetString() is { } uri)
                return uri;

            throw new VideoGenerationException($"Veo tamamlandı ama video uri alanı bulunamadı: {json[..Math.Min(json.Length, 300)]}");
        }

        throw new VideoGenerationException($"Veo video üretimi {maxAttempts * (int)pollInterval.TotalSeconds}sn içinde tamamlanmadı (operation: {operationName}).");
    }

    private static async Task<byte[]> DownloadAsync(HttpClient client, string videoUri, CancellationToken ct)
    {
        try
        {
            return await client.GetByteArrayAsync(videoUri, ct);
        }
        catch (Exception ex)
        {
            throw new VideoGenerationException($"Veo videosu indirilemedi: {ex.Message}", ex);
        }
    }
}
