using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Scrapers;

/// <summary>
/// Scrapes TikTok via the Apify "clockworks/tiktok-scraper" community actor. Apify has no generic
/// "trending" feed for TikTok, so this runs the actor against a configured seed-hashtag list.
/// </summary>
public class TikTokScraper : IScraper
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<TikTokScraper> _logger;

    public TikTokScraper(IHttpClientFactory httpClientFactory, IConfiguration configuration, IUserSecrets secrets, ILogger<TikTokScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _secrets = secrets;
        _logger = logger;
    }

    public Platform Platform => Platform.TikTok;

    public async Task<List<Trend>> GetTrendsAsync(string region = "TR", int maxResults = 20, CancellationToken ct = default)
    {
        var apifyToken = _secrets.ApifyToken;
        if (string.IsNullOrWhiteSpace(apifyToken))
        {
            throw new ScrapingException("TikTok", "Apify API token is not configured (ApiKeys:ApifyApiToken / APIFY_API_TOKEN).");
        }

        var actorId = _configuration["Scraping:Apify:TikTokActorId"] ?? "clockworks/tiktok-scraper";
        var maxHashtags = _configuration.GetValue("Scraping:Apify:MaxHashtagsPerRun", 15);
        var seedsByCategory = ScraperHelpers.LoadSeedHashtagsByCategory(_configuration);
        var hashtags = ScraperHelpers.FlattenSeedHashtags(seedsByCategory, maxHashtags);

        JsonElement root;
        try
        {
            var client = _httpClientFactory.CreateClient("Apify");
            var url = $"v2/acts/{ScraperHelpers.EncodeApifyActorId(actorId)}/run-sync-get-dataset-items?token={apifyToken}";
            var body = new
            {
                hashtags,
                resultsPerPage = maxResults,
                shouldDownloadCovers = false,
                shouldDownloadVideos = false,
            };
            var response = await client.PostAsJsonAsync(url, body, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ScrapingException("TikTok", ex.Message, ex);
        }

        var trends = new List<Trend>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            return trends;
        }

        foreach (var item in root.EnumerateArray())
        {
            try
            {
                var sourceUrl = ScraperHelpers.SafeGetString(item, "webVideoUrl");
                var text = ScraperHelpers.SafeGetString(item, "text");
                if (string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var tags = new List<string>();
                if (item.TryGetProperty("hashtags", out var hashtagsEl) && hashtagsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var h in hashtagsEl.EnumerateArray())
                    {
                        var name = ScraperHelpers.SafeGetString(h, "name");
                        if (!string.IsNullOrWhiteSpace(name)) tags.Add(name);
                    }
                }

                trends.Add(new Trend
                {
                    Title = text.Length > 200 ? text[..200] : text,
                    Description = text,
                    SourceUrl = sourceUrl,
                    SourcePlatform = Platform.TikTok,
                    Category = ScraperHelpers.InferCategory(text + " " + string.Join(" ", tags), seedsByCategory),
                    PublishedAt = ScraperHelpers.SafeGetDateTime(item, "createTimeISO"),
                    ViewCount = ScraperHelpers.SafeGetInt64(item, "playCount"),
                    LikeCount = ScraperHelpers.SafeGetInt64(item, "diggCount"),
                    CommentCount = ScraperHelpers.SafeGetInt64(item, "commentCount"),
                    ShareCount = ScraperHelpers.SafeGetInt64(item, "shareCount"),
                    Tags = JsonSerializer.Serialize(tags),
                    Keywords = string.Join(",", tags),
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping malformed TikTok item");
            }
        }

        return trends;
    }
}
