using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Application.Interfaces.Agents;

/// <summary>
/// Scout Agent: Discovers and scores trending content across platforms.
/// </summary>
public interface IScoutAgent
{
    /// <param name="platforms">
    /// Which platforms to scrape; null = all registered. The automatic pipeline passes [YouTube] only so the
    /// scheduled scan spends no Apify credit (YouTube is the free Data API); the manual "Tara" passes null.
    /// </param>
    Task<List<Trend>> DiscoverTrendsAsync(string region = "TR", IReadOnlyCollection<Platform>? platforms = null, CancellationToken ct = default);
    Task<List<Trend>> GetTopViralTrendsAsync(int topN = 5, string region = "TR", CancellationToken ct = default);

    /// <summary>
    /// Runs the AI-feasibility check over the most viral unprocessed trends and returns them ranked
    /// feasibility-first, virality-second — shared by TrendsController's manual endpoint and
    /// PipelineOrchestrator's automatic run, so "which trend is worth producing" has one definition, not two
    /// that can drift apart. Persists scores so a trend already evaluated isn't re-sent to Gemini unless
    /// <paramref name="force"/> is set.
    /// </summary>
    Task<List<Trend>> SelectBestFeasibleTrendsAsync(int topN = 10, bool force = false, CancellationToken ct = default);
}
