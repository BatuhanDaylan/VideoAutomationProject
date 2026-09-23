using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Media;

/// <summary>
/// Low-level client for aimlapi.com's unified video-generation API (currently backed by Kling, but
/// aimlapi fronts many models behind the same endpoint). Model name is config-driven (Media:AiVideo:Model),
/// never hardcoded, so switching to a different aimlapi-hosted model is a config change, not a code change —
/// same "low-level client, config-driven" layering as GeminiClient (Faz 3).
/// </summary>
public class AiVideoClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<AiVideoClient> _logger;

    public AiVideoClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, IUserSecrets secrets, ILogger<AiVideoClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _secrets = secrets;
        _logger = logger;
    }

    /// <summary>Submits a text-to-video generation request and polls until it completes, returning the video bytes.</summary>
    public async Task<byte[]> GenerateVideoAsync(string prompt, CancellationToken ct = default)
    {
        var token = _secrets.AiVideoToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new VideoGenerationException("AI video üretim token'ı yapılandırılmamış — panelden (🔑 API Anahtarları) gir.");

        var model = _configuration.GetValue("Media:AiVideo:Model", "klingai/v2-master-text-to-video")!;
        var duration = _configuration.GetValue("Media:AiVideo:DurationSeconds", 5);

        var client = _httpClientFactory.CreateClient("AiVideo");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var generationId = await SubmitAsync(client, model, prompt, duration, ct);
        var videoUrl = await PollUntilCompleteAsync(client, generationId, ct);
        return await DownloadAsync(client, videoUrl, ct);
    }

    private async Task<string> SubmitAsync(HttpClient client, string model, string prompt, int duration, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { model, prompt, aspect_ratio = "9:16", duration });

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync("v2/video/generations", new StringContent(body, Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new VideoGenerationException($"AI video isteği başarısız: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new VideoGenerationException($"AI video API {(int)response.StatusCode} döndürdü: {json[..Math.Min(json.Length, 300)]}");

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("id", out var idEl))
            throw new VideoGenerationException($"AI video API yanıtında 'id' yok: {json[..Math.Min(json.Length, 300)]}");

        return idEl.GetString() ?? throw new VideoGenerationException("AI video API boş id döndürdü.");
    }

    private async Task<string> PollUntilCompleteAsync(HttpClient client, string generationId, CancellationToken ct)
    {
        var pollInterval = TimeSpan.FromSeconds(_configuration.GetValue("Media:AiVideo:PollIntervalSeconds", 10));
        var maxAttempts = _configuration.GetValue("Media:AiVideo:MaxPollAttempts", 40);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await Task.Delay(pollInterval, ct);

            var response = await client.GetAsync($"v2/video/generations?generation_id={generationId}", ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new VideoGenerationException($"AI video durum sorgusu {(int)response.StatusCode} döndürdü: {json[..Math.Min(json.Length, 300)]}");

            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;

            _logger.LogDebug("AI video generation {Id} status={Status} (attempt {Attempt}/{Max})", generationId, status, attempt, maxAttempts);

            switch (status)
            {
                case "completed":
                    if (doc.RootElement.TryGetProperty("video", out var video) && video.ValueKind == JsonValueKind.Object
                        && video.TryGetProperty("url", out var urlEl) && urlEl.GetString() is { } url)
                        return url;
                    throw new VideoGenerationException("AI video tamamlandı ama video.url alanı yok.");

                case "error":
                    var errorMessage = doc.RootElement.TryGetProperty("error", out var err) ? err.ToString() : "bilinmeyen hata";
                    throw new VideoGenerationException($"AI video üretimi başarısız: {errorMessage}");
            }
        }

        throw new VideoGenerationException($"AI video üretimi {maxAttempts * (int)pollInterval.TotalSeconds}sn içinde tamamlanmadı (generation id: {generationId}).");
    }

    private static async Task<byte[]> DownloadAsync(HttpClient client, string videoUrl, CancellationToken ct)
    {
        try
        {
            return await client.GetByteArrayAsync(videoUrl, ct);
        }
        catch (Exception ex)
        {
            throw new VideoGenerationException($"AI video indirilemedi: {ex.Message}", ex);
        }
    }
}
