using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Scrapers;

/// <summary>
/// Finds YouTube Shorts via YouTube Data API v3's search.list.
/// Things verified live against the real API before landing on this design (see
/// _skills/_youtube_shorts_hiz_odakli_kesif.md for the full story):
///   1. search.list requires a non-empty `q` — with no query it silently returns zero items regardless
///      of other filters (it's a search endpoint, not a "browse all Shorts" endpoint).
///   2. `order=viewCount` was tried (with a client-side recency cutoff instead of the also-broken
///      `publishedAfter` pairing) and live-tested directly against the API: EVERY result was 300+ days
///      old, several were 3-4+ YEARS old with hundreds of millions of views — YouTube's global view-count
///      ranking is dominated by evergreen giants for any common search term, so no amount of client-side
///      date filtering ever lets a genuinely recent video through. Not usable for "trend potential".
///   3. `order=date` alone with a SMALL result count (the very first approach) only reaches back minutes,
///      so every candidate had near-zero views — but `order=date` with a LARGE result count (tested live:
///      50 results for one seed query) reaches back ~24 hours and DOES surface videos already gaining real
///      traction (live-verified: top result was 222,945 views in 17.3 hours = ~12,860 views/hour — the
///      same shape of signal TikTok's scraper already returns natively). So the fix isn't the sort order,
///      it's the candidate pool size — MaxResultsPerQuery is decoupled from the Apify-cost-driven
///      Scraping:MaxResultsPerPlatform for exactly this reason (YouTube's API is quota-limited, not $-limited).
///   4. `chart=mostPopular` (an even earlier approach) never contains Shorts at all — checked 49 TR
///      results, 0 were ≤60s.
/// So: search.list (multi-query, Turkish seeds, order=date, large maxResults) finds a big pool of genuinely
/// recent candidates; a second videos.list call (batched into &lt;=50 IDs per request — the API rejects
/// more, which the larger pool here made a real bug once) fetches statistics/contentDetails (search.list
/// only returns part=snippet); contentDetails.duration confirms each result is actually ≤60s
/// (videoDuration=short only guarantees &lt;4min). The per-hour velocity gate/scoring downstream
/// (ScoutAgent) then picks the genuinely fastest risers out of that pool, same as TikTok/Instagram.
/// Transcript fetching is NOT done here (unlike an earlier version): with ~200 raw candidates per scan
/// now instead of ~15, fetching a transcript for every one of them started getting YouTube's (unofficial,
/// undocumented) transcript endpoint to rate-limit/block our IP — live-observed as a wrong-looking
/// "transcript" that was actually a stringified ConnectionError. Same fix as TikTok already had: transcript
/// is fetched by AnalyzerAgent, once, only for the single trend actually selected for analysis.
/// </summary>
public class YouTubeScraper : IScraper
{
    private static readonly Dictionary<string, TrendCategory> CategoryMap = new()
    {
        ["10"] = TrendCategory.Music,
        ["17"] = TrendCategory.Sports,
        ["20"] = TrendCategory.Gaming,
        ["22"] = TrendCategory.Lifestyle,
        ["23"] = TrendCategory.Comedy,
        ["24"] = TrendCategory.Entertainment,
        ["25"] = TrendCategory.News,
        ["27"] = TrendCategory.Education,
        ["28"] = TrendCategory.Technology,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<YouTubeScraper> _logger;

    public YouTubeScraper(IHttpClientFactory httpClientFactory, IConfiguration configuration, IUserSecrets secrets, ILogger<YouTubeScraper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _secrets = secrets;
        _logger = logger;
    }

    public Platform Platform => Platform.YouTube;

    public async Task<List<Trend>> GetTrendsAsync(string region = "TR", int maxResults = 20, CancellationToken ct = default)
    {
        var apiKey = _secrets.YouTubeApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ScrapingException("YouTube", "YouTube API key is not configured (ApiKeys:YouTubeApiKey / YOUTUBE_API_KEY).");
        }

        var client = _httpClientFactory.CreateClient("YouTube");
        var language = _configuration.GetValue("Pipeline:Language", "tr")!;
        var maxQueries = _configuration.GetValue("Scraping:YouTube:MaxSeedQueriesPerRun", 5);
        // Decoupled from Scraping:MaxResultsPerPlatform (the shared, Apify-cost-driven budget knob) —
        // the YouTube Data API is quota-limited, not billed in the $ sense TikTok/Instagram are, so a
        // wider per-query result count here doesn't touch the Apify budget the user is managing.
        var maxResultsPerQuery = _configuration.GetValue("Scraping:YouTube:MaxResultsPerQuery", 50);
        var seedsByCategory = ScraperHelpers.LoadSeedHashtagsByCategory(_configuration);
        var queries = ScraperHelpers.FlattenSeedHashtags(seedsByCategory, maxQueries);

        var videoIds = new List<string>();
        try
        {
            foreach (var seed in queries)
            {
                var searchUrl =
                    $"youtube/v3/search?part=snippet&type=video&videoDuration=short&order=date" +
                    $"&q={Uri.EscapeDataString(seed + " shorts")}&regionCode={Uri.EscapeDataString(region)}" +
                    $"&relevanceLanguage={Uri.EscapeDataString(language)}&maxResults={maxResultsPerQuery}&key={apiKey}";
                var searchResponse = await client.GetAsync(searchUrl, ct);
                searchResponse.EnsureSuccessStatusCode();
                videoIds.AddRange(ExtractVideoIds(await searchResponse.Content.ReadAsStringAsync(ct)));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ScrapingException("YouTube", ex.Message, ex);
        }

        videoIds = videoIds.Distinct().ToList();
        if (videoIds.Count == 0) return new List<Trend>();

        var trends = new List<Trend>();

        // videos.list rejects more than 50 IDs per call (400 Bad Request) — with the larger candidate
        // pool now used (up to MaxSeedQueriesPerRun x MaxResultsPerQuery raw IDs), this batching is
        // required, not optional; it wasn't needed back when the pool was small enough to always fit.
        foreach (var idBatch in videoIds.Chunk(50))
        {
            string detailsJson;
            try
            {
                var detailsUrl = $"youtube/v3/videos?part=snippet,statistics,contentDetails&id={string.Join(",", idBatch)}&key={apiKey}";
                var detailsResponse = await client.GetAsync(detailsUrl, ct);
                detailsResponse.EnsureSuccessStatusCode();
                detailsJson = await detailsResponse.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw new ScrapingException("YouTube", ex.Message, ex);
            }

            using var doc = JsonDocument.Parse(detailsJson);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in items.EnumerateArray())
            {
                try
                {
                    var videoId = ScraperHelpers.SafeGetString(item, "id");
                    if (string.IsNullOrWhiteSpace(videoId))
                    {
                        continue;
                    }

                    var contentDetails = item.TryGetProperty("contentDetails", out var cd) ? cd : default;
                    var durationIso = ScraperHelpers.SafeGetString(contentDetails, "duration");
                    if (!TryParseIso8601Duration(durationIso, out var duration) || duration.TotalSeconds > 60)
                    {
                        continue; // videoDuration=short only guarantees <4min — confirm it's genuinely a Short
                    }

                    var snippet = item.TryGetProperty("snippet", out var sn) ? sn : default;
                    var statistics = item.TryGetProperty("statistics", out var st) ? st : default;

                    var tags = new List<string>();
                    if (snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in tagsEl.EnumerateArray())
                        {
                            var tag = t.GetString();
                            if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
                        }
                    }

                    var categoryId = ScraperHelpers.SafeGetString(snippet, "categoryId");
                    var category = CategoryMap.TryGetValue(categoryId, out var mapped) ? mapped : TrendCategory.Other;

                    trends.Add(new Trend
                    {
                        Title = ScraperHelpers.SafeGetString(snippet, "title"),
                        Description = ScraperHelpers.SafeGetString(snippet, "description"),
                        SourceUrl = $"https://www.youtube.com/watch?v={videoId}",
                        SourcePlatform = Platform.YouTube,
                        Category = category,
                        PublishedAt = ScraperHelpers.SafeGetDateTime(snippet, "publishedAt"),
                        // Uploader-declared, so frequently absent — treated as a strong hint when present,
                        // never as a requirement (ScoutAgent falls back to text heuristics when it's null).
                        LanguageCode = FirstNonEmpty(
                            ScraperHelpers.SafeGetString(snippet, "defaultAudioLanguage"),
                            ScraperHelpers.SafeGetString(snippet, "defaultLanguage")),
                        ViewCount = ScraperHelpers.SafeGetInt64(statistics, "viewCount"),
                        LikeCount = ScraperHelpers.SafeGetInt64(statistics, "likeCount"),
                        CommentCount = ScraperHelpers.SafeGetInt64(statistics, "commentCount"),
                        ShareCount = 0, // not exposed by the Data API
                        Tags = JsonSerializer.Serialize(tags),
                        Keywords = string.Join(",", tags),
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping malformed YouTube video item");
                }
            }
        }

        return trends;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static List<string> ExtractVideoIds(string json)
    {
        var ids = new List<string>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return ids;

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idEl) && idEl.TryGetProperty("videoId", out var vidEl))
            {
                var id = vidEl.GetString();
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
            }
        }

        return ids;
    }

    private static bool TryParseIso8601Duration(string iso8601, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(iso8601)) return false;

        try
        {
            duration = System.Xml.XmlConvert.ToTimeSpan(iso8601);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

}
