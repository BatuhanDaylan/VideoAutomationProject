using System.Text.Json;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Infrastructure.Scrapers;

namespace VideoAutomation.Infrastructure.Publishing;

/// <summary>
/// Fetches PUBLIC stats (views/likes/comments) for already-published YouTube videos via the Data API's
/// <c>videos.list?part=statistics</c> — only needs the current user's API key (IUserSecrets.YouTubeApiKey),
/// NOT OAuth: public statistics are readable with a plain key. Deeper metrics (retention/CTR) would need the
/// Analytics API + channel ownership and are intentionally out of scope here.
/// Never throws for a missing key / transport error — returns an empty map so the caller degrades gracefully
/// (same philosophy as the scrapers).
/// </summary>
public class YouTubeStatsService
{
    public readonly record struct VideoStats(long ViewCount, long LikeCount, long CommentCount);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<YouTubeStatsService> _logger;

    public YouTubeStatsService(IHttpClientFactory httpClientFactory, IUserSecrets secrets, ILogger<YouTubeStatsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secrets = secrets;
        _logger = logger;
    }

    /// <summary>Returns stats keyed by YouTube video id. Ids with no data (deleted/private) are simply absent.</summary>
    public async Task<Dictionary<string, VideoStats>> GetStatsAsync(IReadOnlyCollection<string> videoIds, CancellationToken ct = default)
    {
        var result = new Dictionary<string, VideoStats>();
        var apiKey = _secrets.YouTubeApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) || videoIds.Count == 0) return result;

        var client = _httpClientFactory.CreateClient("YouTube");
        var distinct = videoIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();

        // videos.list rejects more than 50 ids per call (same limit the scraper hit) — batch in 50s.
        foreach (var batch in distinct.Chunk(50))
        {
            try
            {
                var url = $"youtube/v3/videos?part=statistics&id={string.Join(",", batch)}&key={apiKey}";
                var resp = await client.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("YouTube stats {Status} for {Count} ids", resp.StatusCode, batch.Length);
                    continue;
                }

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in items.EnumerateArray())
                {
                    var id = ScraperHelpers.SafeGetString(item, "id");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var stats = item.TryGetProperty("statistics", out var st) ? st : default;
                    result[id] = new VideoStats(
                        ScraperHelpers.SafeGetInt64(stats, "viewCount"),
                        ScraperHelpers.SafeGetInt64(stats, "likeCount"),
                        ScraperHelpers.SafeGetInt64(stats, "commentCount"));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex, "YouTube stats batch başarısız");
            }
        }

        return result;
    }
}
