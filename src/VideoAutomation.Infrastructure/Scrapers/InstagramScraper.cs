using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Scrapers;

/// <summary>
/// Scrapes Instagram via the Apify "apify/instagram-hashtag-scraper" community actor, run against a
/// configured seed-hashtag list (same rationale as TikTokScraper — no generic "trending" feed exists).
/// </summary>
public class InstagramScraper : IScraper
{
    private static readonly Regex HashtagPattern = new(@"#(\w+)", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<InstagramScraper> _logger;

    public InstagramScraper(IHttpClientFactory httpClientFactory, IConfiguration configuration, IUserSecrets secrets, ILogger<InstagramScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _secrets = secrets;
        _logger = logger;
    }

    public Platform Platform => Platform.Instagram;

    public async Task<List<Trend>> GetTrendsAsync(string region = "TR", int maxResults = 20, CancellationToken ct = default)
    {
        var apifyToken = _secrets.ApifyToken;
        if (string.IsNullOrWhiteSpace(apifyToken))
        {
            throw new ScrapingException("Instagram", "Apify API token is not configured (ApiKeys:ApifyApiToken / APIFY_API_TOKEN).");
        }

        var actorId = _configuration["Scraping:Apify:InstagramActorId"] ?? "apify/instagram-hashtag-scraper";
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
                resultsLimit = maxResults,
            };
            var response = await client.PostAsJsonAsync(url, body, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ScrapingException("Instagram", ex.Message, ex);
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
                var sourceUrl = ScraperHelpers.SafeGetString(item, "url");
                var caption = ScraperHelpers.SafeGetString(item, "caption");
                if (string.IsNullOrWhiteSpace(sourceUrl))
                {
                    continue;
                }

                var tags = new List<string>();
                if (item.TryGetProperty("hashtags", out var hashtagsEl) && hashtagsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var h in hashtagsEl.EnumerateArray())
                    {
                        if (h.ValueKind == JsonValueKind.String)
                        {
                            var name = h.GetString();
                            if (!string.IsNullOrWhiteSpace(name)) tags.Add(name!);
                        }
                    }
                }
                if (tags.Count == 0 && !string.IsNullOrWhiteSpace(caption))
                {
                    // Fallback: the actor's hashtags field can be absent — captions still contain "#tag" tokens.
                    tags.AddRange(HashtagPattern.Matches(caption).Select(m => m.Groups[1].Value).Distinct());
                }

                // videoViewCount is only present on Reels/video posts; image posts have no view metric,
                // so ViewCount stays 0 there — ViralityScorer must guard divide-by-zero against this.
                trends.Add(new Trend
                {
                    Title = caption.Length > 200 ? caption[..200] : caption,
                    Description = caption,
                    SourceUrl = sourceUrl,
                    SourcePlatform = Platform.Instagram,
                    Category = ScraperHelpers.InferCategory(caption + " " + string.Join(" ", tags), seedsByCategory),
                    PublishedAt = ScraperHelpers.SafeGetDateTime(item, "timestamp"),
                    ViewCount = ScraperHelpers.SafeGetInt64(item, "videoViewCount"),
                    LikeCount = ScraperHelpers.SafeGetInt64(item, "likesCount"),
                    CommentCount = ScraperHelpers.SafeGetInt64(item, "commentsCount"),
                    ShareCount = 0, // not exposed by this actor
                    Tags = JsonSerializer.Serialize(tags),
                    Keywords = string.Join(",", tags),
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping malformed Instagram item");
            }
        }

        return trends;
    }
}
