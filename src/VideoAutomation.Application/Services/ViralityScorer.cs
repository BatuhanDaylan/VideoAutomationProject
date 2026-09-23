using Microsoft.Extensions.Configuration;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.ValueObjects;

namespace VideoAutomation.Application.Services;

/// <summary>
/// Computes ViralityScore for a Trend. Pure logic, no I/O — all inputs come from the Trend itself
/// and configuration.
/// </summary>
public class ViralityScorer : IViralityScorer
{
    private readonly IConfiguration _configuration;

    public ViralityScorer(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<ViralityScore> CalculateScoreAsync(Trend trend, CancellationToken ct = default)
    {
        var hoursSincePublished = trend.PublishedAt.HasValue
            ? Math.Max((DateTime.UtcNow - trend.PublishedAt.Value).TotalHours, 0.5)
            : 24.0; // unknown publish time (e.g. some Instagram items) — assume a day old

        var viewVelocity = trend.ViewCount / hoursSincePublished;

        // v1 heuristic: views vs. a configurable per-platform baseline. NOT based on real channel/account
        // history — refine later once per-channel baselines are tracked (out of scope for Faz 2).
        var baseline = _configuration.GetValue<double?>($"Scraping:Virality:BaselineViewsByPlatform:{trend.SourcePlatform}") ?? 30000;
        var outlierFactor = trend.ViewCount / Math.Max(baseline, 1);

        var engagementRate = trend.ViewCount > 0
            ? (trend.LikeCount + trend.CommentCount + trend.ShareCount) / (double)trend.ViewCount
            : 0;

        // v1 placeholder proxy for momentum: no real trend-momentum signal (e.g. Google Trends) is
        // integrated yet. Blends recency and engagement until a real signal is added in a later phase.
        var freshnessFactor = trend.PublishedAt.HasValue
            ? Math.Clamp(1 - hoursSincePublished / 72.0, 0, 1)
            : 0.5;
        var engagementFactor = Math.Clamp(engagementRate * 10, 0, 1);
        var trendMomentum = Math.Round(60 * freshnessFactor + 40 * engagementFactor, 2);

        // Carries forward whatever ScoutAgent pre-seeded via cross-platform matching.
        var crossPlatformCount = Math.Clamp(trend.ViralityScore?.CrossPlatformCount ?? 1, 1, 3);

        // What counts as "maxed out" velocity/outlier varies a lot by platform and discovery method — a
        // YouTube Short found via keyword search on small channels realistically tops out far lower than a
        // genuinely viral TikTok. Without this, scores end up compressed near the bottom (everything looks
        // "Low/Moderate") whenever the discovery method's typical scale is smaller than these caps assume.
        var velocityCap = _configuration.GetValue<double?>($"Scraping:Virality:VelocityCapPerHourByPlatform:{trend.SourcePlatform}") ?? 10000;
        var outlierCap = _configuration.GetValue<double?>($"Scraping:Virality:OutlierMultiplierCapByPlatform:{trend.SourcePlatform}") ?? 20;

        return Task.FromResult(new ViralityScore
        {
            TrendMomentum = trendMomentum,
            ViewVelocity = viewVelocity,
            OutlierFactor = outlierFactor,
            EngagementRate = engagementRate,
            CrossPlatformCount = crossPlatformCount,
            VelocityCapPerHour = velocityCap,
            OutlierMultiplierCap = outlierCap,
        });
    }

    public async Task<List<Trend>> RankTrendsAsync(List<Trend> trends, CancellationToken ct = default)
    {
        foreach (var trend in trends)
        {
            var score = await CalculateScoreAsync(trend, ct);
            trend.ViralityScore = score;
            trend.FinalViralityScore = score.FinalScore;
        }

        return trends.OrderByDescending(t => t.FinalViralityScore).ToList();
    }
}
