using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Media;

/// <summary>IStockVideoProvider backed by the Pexels Video Search API.</summary>
public class PexelsStockVideoProvider : IStockVideoProvider
{
    public VisualMode SupportedMode => VisualMode.StockVideo;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<PexelsStockVideoProvider> _logger;

    public PexelsStockVideoProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, IUserSecrets secrets, ILogger<PexelsStockVideoProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _secrets = secrets;
        _logger = logger;
    }

    public async Task<(string videoPathA, string videoPathB)> GetDualVideosAsync(string keyword, string outputDir, CancellationToken ct = default)
    {
        var links = await SearchAsync(keyword, perPage: 3, ct);
        if (links.Count == 0)
            throw new VideoGenerationException($"Pexels'te '{keyword}' için sonuç bulunamadı.");

        if (links.Count == 1)
            _logger.LogWarning("Pexels '{Keyword}' için tek sonuç döndürdü, A/B split aynı klibi kullanacak", keyword);

        var pathA = await DownloadAsync(links[0], outputDir, ct);
        var pathB = links.Count > 1 ? await DownloadAsync(links[1], outputDir, ct) : pathA;
        return (pathA, pathB);
    }

    public async Task<string> GetSingleVideoAsync(string keyword, string outputDir, CancellationToken ct = default)
    {
        var links = await SearchAsync(keyword, perPage: 1, ct);
        if (links.Count == 0)
            throw new VideoGenerationException($"Pexels'te '{keyword}' için sonuç bulunamadı.");

        return await DownloadAsync(links[0], outputDir, ct);
    }

    private async Task<List<string>> SearchAsync(string keyword, int perPage, CancellationToken ct)
    {
        var apiKey = _secrets.PexelsKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new VideoGenerationException("Pexels API key yapılandırılmamış — panelden (🔑 API Anahtarları) gir.");

        var orientation = _configuration.GetValue("Media:Pexels:Orientation", "portrait");
        var client = _httpClientFactory.CreateClient("Pexels");
        client.DefaultRequestHeaders.Add("Authorization", apiKey);

        var url = $"videos/search?query={Uri.EscapeDataString(keyword)}&per_page={perPage}&orientation={orientation}";

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new VideoGenerationException($"Pexels isteği başarısız: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new VideoGenerationException($"Pexels API {(int)response.StatusCode} döndürdü: {json[..Math.Min(json.Length, 200)]}");

        return ExtractBestVideoLinks(json);
    }

    /// <summary>Picks the mp4 video_files entry closest to 1080x1920 for each search result.</summary>
    private static List<string> ExtractBestVideoLinks(string json)
    {
        var links = new List<string>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("videos", out var videos) || videos.ValueKind != JsonValueKind.Array)
            return links;

        foreach (var video in videos.EnumerateArray())
        {
            if (!video.TryGetProperty("video_files", out var files) || files.ValueKind != JsonValueKind.Array) continue;

            string? bestLink = null;
            var bestScore = long.MaxValue;
            foreach (var file in files.EnumerateArray())
            {
                var fileType = file.TryGetProperty("file_type", out var ft) ? ft.GetString() : null;
                if (fileType != "video/mp4") continue;

                var width = file.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetInt64() : 0;
                var height = file.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt64() : 0;

                // Prefer files that already cover 1080x1920 (tier 0, smallest excess wins — no upscale
                // needed downstream) over anything smaller that would have to be stretched up and go soft
                // (tier 1, ordered by how far short it falls). Tier 0 always beats tier 1.
                var coversTarget = width >= 1080 && height >= 1920;
                var tier = coversTarget ? 0L : 1L;
                var magnitude = coversTarget ? (width - 1080) + (height - 1920) : Math.Abs(width - 1080) + Math.Abs(height - 1920);
                var score = tier * 100_000_000L + magnitude;

                if (score < bestScore && file.TryGetProperty("link", out var link))
                {
                    bestScore = score;
                    bestLink = link.GetString();
                }
            }

            if (!string.IsNullOrWhiteSpace(bestLink)) links.Add(bestLink);
        }

        return links;
    }

    private async Task<string> DownloadAsync(string url, string outputDir, CancellationToken ct)
    {
        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, $"pexels-{Guid.NewGuid():N}.mp4");

        try
        {
            var client = _httpClientFactory.CreateClient("Pexels");
            await using var stream = await client.GetStreamAsync(url, ct);
            await using var file = File.Create(path);
            await stream.CopyToAsync(file, ct);
        }
        catch (Exception ex)
        {
            throw new VideoGenerationException($"Pexels videosu indirilemedi: {ex.Message}", ex);
        }

        return path;
    }
}
