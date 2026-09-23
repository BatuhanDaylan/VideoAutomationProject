using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Domain.ValueObjects;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Infrastructure.Scrapers;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Agents;

/// <summary>
/// Scout Agent: Discovers and scores trending content across platforms.
/// Lives in Infrastructure (not Application/Services/Agents as originally sketched) because it needs
/// direct AppDbContext access to persist/read trends, and Infrastructure already references Application —
/// the reverse reference would be circular.
/// </summary>
public class ScoutAgent : IScoutAgent
{
    private static readonly Regex TokenPattern = new("[^a-z0-9]+", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "bir", "bu", "ve", "ile", "icin", "cok", "the", "and", "for", "with", "are", "was", "this", "that",
    };

    private readonly IEnumerable<IScraper> _scrapers;
    private readonly IViralityScorer _scorer;
    private readonly IContentGenerator _contentGenerator;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScoutAgent> _logger;

    public ScoutAgent(IEnumerable<IScraper> scrapers, IViralityScorer scorer, IContentGenerator contentGenerator, AppDbContext db, IConfiguration configuration, ILogger<ScoutAgent> logger)
    {
        _scrapers = scrapers;
        _scorer = scorer;
        _contentGenerator = contentGenerator;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<Trend>> DiscoverTrendsAsync(string region = "TR", IReadOnlyCollection<Platform>? platforms = null, CancellationToken ct = default)
    {
        var maxResults = _configuration.GetValue("Scraping:MaxResultsPerPlatform", 20);

        // platforms == null → all registered scrapers (manual "Tara"). The AUTOMATIC pipeline passes
        // [YouTube] so the scheduled cron never spends Apify credit on TikTok/Instagram — those (handled
        // manually anyway) are scraped only on-demand when the user explicitly scans. YouTube uses the free
        // Data API, so the daily auto scan costs nothing.
        var activeScrapers = platforms == null
            ? _scrapers
            : _scrapers.Where(s => platforms.Contains(s.Platform));

        var scraperTasks = activeScrapers.Select(async scraper =>
        {
            try
            {
                return await scraper.GetTrendsAsync(region, maxResults, ct);
            }
            catch (ScrapingException ex)
            {
                _logger.LogWarning(ex, "Scraper {Platform} failed, continuing with partial results", scraper.Platform);
                return new List<Trend>();
            }
        });

        var results = await Task.WhenAll(scraperTasks);
        var allTrends = results.SelectMany(r => r).DistinctBy(t => t.SourceUrl).ToList();

        if (allTrends.Count == 0)
        {
            _logger.LogWarning("Trend discovery found no trends across any platform for region {Region}", region);
            return new List<Trend>();
        }

        var similarityThreshold = _configuration.GetValue("Scraping:CrossPlatformSimilarityThreshold", 0.3);
        ApplyCrossPlatformMatching(allTrends, similarityThreshold);

        var candidates = FilterByTrendPotential(allTrends);
        if (candidates.Count == 0)
        {
            _logger.LogWarning("All {Count} scraped trends fell below the trend-potential velocity threshold for region {Region}", allTrends.Count, region);
            return new List<Trend>();
        }

        candidates = FilterByLanguage(candidates);
        if (candidates.Count == 0)
        {
            _logger.LogWarning("No trends left after the language filter for region {Region}", region);
            return new List<Trend>();
        }

        var ranked = await _scorer.RankTrendsAsync(candidates, ct);

        return await UpsertTrendsAsync(ranked, ct);
    }

    /// <summary>
    /// Gate before scoring: a video with a huge view count accumulated slowly over weeks isn't a "trend"
    /// in the sense this pipeline cares about — only genuinely fast-rising candidates (views/hour above a
    /// per-platform floor) are worth spending script/video generation effort on. Trends that don't clear
    /// this bar are simply dropped from this scan (not deleted from DB if already stored from an earlier
    /// scan — they just won't be refreshed/re-ranked, and age out of relevance naturally).
    /// </summary>
    private List<Trend> FilterByTrendPotential(List<Trend> trends)
    {
        var filtered = trends.Where(t =>
        {
            var hoursSincePublished = t.PublishedAt.HasValue
                ? Math.Max((DateTime.UtcNow - t.PublishedAt.Value).TotalHours, 0.5)
                : 24.0;
            var velocity = t.ViewCount / hoursSincePublished;
            var minVelocity = _configuration.GetValue<double?>($"Scraping:TrendPotential:MinViewVelocityPerHour:{t.SourcePlatform}") ?? 0;
            _logger.LogDebug("Candidate [{Platform}] velocity={Velocity:N0}/hr (views={Views:N0}, hoursSince={Hours:N1}) threshold={Threshold:N0} title={Title}",
                t.SourcePlatform, velocity, t.ViewCount, hoursSincePublished, minVelocity, t.Title.Length > 60 ? t.Title[..60] : t.Title);
            return velocity >= minVelocity;
        }).ToList();

        if (filtered.Count < trends.Count)
            _logger.LogInformation("Trend-potential filter: {Kept}/{Total} candidates kept (velocity threshold)", filtered.Count, trends.Count);

        return filtered;
    }

    /// <summary>
    /// Drops candidates that aren't in the pipeline's target language. The generated video is narrated in
    /// Turkish (Pipeline:Language) by a Turkish TTS voice, so a foreign-language source trend would have to
    /// be translated rather than adapted — losing exactly the wordplay/cultural context that made it trend.
    /// Disabled by setting Scraping:LanguageFilter:Enabled=false.
    /// </summary>
    private List<Trend> FilterByLanguage(List<Trend> trends)
    {
        if (!_configuration.GetValue("Scraping:LanguageFilter:Enabled", true))
            return trends;

        var language = _configuration.GetValue("Pipeline:Language", "tr")!;
        if (!language.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
        {
            // Only Turkish detection is implemented; for any other target language, filtering would
            // silently drop everything, so it's skipped rather than applied incorrectly.
            _logger.LogDebug("Language filter skipped: no detector for target language {Language}", language);
            return trends;
        }

        var filtered = trends
            .Where(t => LanguageHeuristics.IsTurkish($"{t.Title} {t.Description}", t.LanguageCode))
            .ToList();

        if (filtered.Count < trends.Count)
            _logger.LogInformation("Language filter: {Kept}/{Total} candidates kept (target={Language})",
                filtered.Count, trends.Count, language);

        return filtered;
    }

    public async Task<List<Trend>> SelectBestFeasibleTrendsAsync(int topN = 10, bool force = false, CancellationToken ct = default)
    {
        // AUTOMATIC pipeline is YouTube-only by design: only YouTube lets Gemini understand a video's real
        // content for free (multimodal via URL, no download). TikTok/Instagram content understanding would
        // require downloading + uploading each clip — too costly to run unattended — so those are handled
        // MANUALLY instead (user describes the video → POST /api/trends/{id}/analyze-manual). The auto
        // selection therefore never picks a TikTok/IG trend.
        // Look deeper than topN so a run doesn't dead-end just because the very top-viral trends happen to
        // be infeasible — feasible ones may sit a bit lower in the virality ranking.
        var poolSize = Math.Max(topN * 3, 15);
        var candidates = await _db.Trends
            .Where(t => !t.IsProcessed && t.SourcePlatform == Platform.YouTube)
            .OrderByDescending(t => t.FinalViralityScore)
            .Take(poolSize)
            .ToListAsync(ct);

        if (candidates.Count == 0) return [];

        var minScore = _configuration.GetValue("Pipeline:MinAiFeasibilityScore", 60);
        var maxWatch = _configuration.GetValue("Pipeline:MaxFeasibilityWatchesPerRun", 8);
        var feasible = new List<Trend>();
        var watched = 0;

        // Score feasibility from the ACTUAL video (multimodal, Gemini watches the URL) — NOT the hashtag
        // title. Title-only scoring rated nearly every YouTube Short below the bar (a comedy/reaction title
        // "looks" infeasible), so the pipeline dead-ended with "no feasible trend" even with 150+ candidates.
        // Watch in virality order, cap the number of watches for cost, and stop once we have enough. Each
        // watch also fills ContentSummary, which the later analysis step reuses (so no double watch), and the
        // cached score means a trend is never re-watched on a future run.
        foreach (var trend in candidates)
        {
            if (feasible.Count >= topN) break;

            if (force || !trend.AiFeasibilityScore.HasValue || string.IsNullOrWhiteSpace(trend.ContentSummary))
            {
                if (watched >= maxWatch) break;
                watched++;
                var analysis = await _contentGenerator.AnalyzeYouTubeVideoAsync(trend.SourceUrl, ct);
                if (analysis != null)
                {
                    trend.ContentSummary = analysis.Summary;
                    trend.AiFeasibilityScore = analysis.FeasibilityScore;
                    trend.AiFeasibilityReason = analysis.FeasibilityReason;
                    await _db.SaveChangesAsync(ct);
                    _logger.LogInformation("Trend {Id} multimodal uygunluk: {Score} — {Reason}",
                        trend.Id, analysis.FeasibilityScore, analysis.FeasibilityReason);
                }
            }

            if ((trend.AiFeasibilityScore ?? 0) >= minScore)
                feasible.Add(trend);
        }

        // Ranked by feasibility first, virality second: among trends we can actually produce, pick the best.
        var ranked = feasible
            .OrderByDescending(t => t.AiFeasibilityScore)
            .ThenByDescending(t => t.FinalViralityScore)
            .ToList();

        _logger.LogInformation("Uygunluk taraması: {Watched} video izlendi, {Feasible} uygun (>= {Min}) trend bulundu",
            watched, ranked.Count, minScore);

        return ranked;
    }

    /// <remarks>
    /// <paramref name="region"/> is accepted for interface symmetry but not applied as a filter — Trend
    /// has no persisted Region column (the app is single-region today, Pipeline:Region=TR). Add one if
    /// multi-region support is needed later.
    /// </remarks>
    public async Task<List<Trend>> GetTopViralTrendsAsync(int topN = 5, string region = "TR", CancellationToken ct = default)
    {
        return await _db.Trends
            .Where(t => !t.IsProcessed)
            .OrderByDescending(t => t.FinalViralityScore)
            .Take(topN)
            .ToListAsync(ct);
    }

    private async Task<List<Trend>> UpsertTrendsAsync(List<Trend> trends, CancellationToken ct)
    {
        // Collapse duplicate SourceUrls within this batch first — a single scan can surface the same video
        // through two different seed queries; without this we'd insert duplicate rows for it.
        var batch = trends
            .GroupBy(t => t.SourceUrl)
            .Select(g => g.First())
            .ToList();

        var urls = batch.Select(t => t.SourceUrl).ToList();
        // Load as a LIST then group — NOT ToDictionaryAsync: the Trends table may already contain duplicate
        // SourceUrls from older runs, and ToDictionaryAsync throws "same key already added" on the first
        // collision (this crashed the whole scan). Keep the first row per URL as the canonical one to update;
        // stray duplicates are left in place (a separate cleanup prunes the unreferenced ones).
        var existingByUrl = (await _db.Trends
                .Where(t => urls.Contains(t.SourceUrl))
                .ToListAsync(ct))
            .GroupBy(t => t.SourceUrl)
            .ToDictionary(g => g.Key, g => g.First());

        // Returns the tracked/added entities, not the input list — an update path that returned the
        // original unpersisted objects instead would hand back stale duplicates for existing rows.
        var persisted = new List<Trend>(batch.Count);
        foreach (var trend in batch)
        {
            if (existingByUrl.TryGetValue(trend.SourceUrl, out var existingTrend))
            {
                existingTrend.Title = trend.Title;
                existingTrend.Description = trend.Description;
                existingTrend.Category = trend.Category;
                existingTrend.PublishedAt = trend.PublishedAt;
                existingTrend.Transcript = trend.Transcript;
                existingTrend.LanguageCode = trend.LanguageCode;
                existingTrend.ViewCount = trend.ViewCount;
                existingTrend.LikeCount = trend.LikeCount;
                existingTrend.CommentCount = trend.CommentCount;
                existingTrend.ShareCount = trend.ShareCount;
                existingTrend.Tags = trend.Tags;
                existingTrend.Keywords = trend.Keywords;
                existingTrend.ViralityScore = trend.ViralityScore;
                existingTrend.FinalViralityScore = trend.FinalViralityScore;
                // IsProcessed / IsSelected intentionally left untouched — a rescan must not clobber user state.
                persisted.Add(existingTrend);
            }
            else
            {
                _db.Trends.Add(trend);
                persisted.Add(trend);
            }
        }

        await _db.SaveChangesAsync(ct);
        return persisted;
    }

    /// <summary>
    /// Estimates how many distinct platforms cover the same real-world trend by comparing normalized
    /// title/keyword token overlap (Jaccard similarity) across trends from different platforms. There is
    /// no shared ID space across YouTube/TikTok/Instagram, so this is a best-effort text match, not exact.
    /// Sets CrossPlatformCount on each trend's (pre-seeded) ViralityScore before scoring runs.
    /// </summary>
    // internal static (not private instance) so it's unit-testable without constructing the whole agent:
    // the threshold — the only thing it needed _configuration for — is now passed in, leaving a pure
    // function of (trends, threshold). Caller (DiscoverTrendsAsync) reads the config value and passes it.
    internal static void ApplyCrossPlatformMatching(List<Trend> trends, double threshold)
    {
        var tokenSets = trends.Select(Tokenize).ToList();

        for (var i = 0; i < trends.Count; i++)
        {
            var matchedPlatforms = new HashSet<Platform> { trends[i].SourcePlatform };

            for (var j = 0; j < trends.Count && matchedPlatforms.Count < 3; j++)
            {
                if (i == j || trends[j].SourcePlatform == trends[i].SourcePlatform) continue;
                if (matchedPlatforms.Contains(trends[j].SourcePlatform)) continue;

                if (JaccardSimilarity(tokenSets[i], tokenSets[j]) >= threshold)
                {
                    matchedPlatforms.Add(trends[j].SourcePlatform);
                }
            }

            trends[i].ViralityScore = new ViralityScore { CrossPlatformCount = Math.Clamp(matchedPlatforms.Count, 1, 3) };
        }
    }

    private static HashSet<string> Tokenize(Trend trend)
    {
        var text = NormalizeTurkish($"{trend.Title} {trend.Keywords}").ToLowerInvariant();
        return TokenPattern.Split(text)
            .Where(t => t.Length >= 3 && !StopWords.Contains(t))
            .ToHashSet();
    }

    private static string NormalizeTurkish(string text) => text
        .Replace('ı', 'i').Replace('İ', 'I')
        .Replace('ğ', 'g').Replace('Ğ', 'G')
        .Replace('ü', 'u').Replace('Ü', 'U')
        .Replace('ş', 's').Replace('Ş', 'S')
        .Replace('ö', 'o').Replace('Ö', 'O')
        .Replace('ç', 'c').Replace('Ç', 'C');

    internal static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }
}
