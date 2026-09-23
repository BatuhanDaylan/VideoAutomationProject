namespace VideoAutomation.Domain.ValueObjects;

/// <summary>
/// Value object that calculates virality potential based on multiple signals:
/// view velocity, outlier detection, engagement rate, cross-platform presence, and trend momentum.
/// </summary>
public class ViralityScore
{
    /// <summary>Google Trends momentum signal (0-100)</summary>
    public double TrendMomentum { get; set; }

    /// <summary>How fast views are growing (views per hour)</summary>
    public double ViewVelocity { get; set; }

    /// <summary>How many times above the channel's average (3x-20x = outlier)</summary>
    public double OutlierFactor { get; set; }

    /// <summary>Engagement rate: (likes + comments + shares) / views</summary>
    public double EngagementRate { get; set; }

    /// <summary>Number of platforms where this topic is trending (1-3)</summary>
    public int CrossPlatformCount { get; set; }

    /// <summary>
    /// Views/hour that counts as a "maxed out" (100%) velocity score. Platform- and discovery-method
    /// dependent — set by ViralityScorer from config, not a universal constant: a YouTube Short found via
    /// organic keyword search on small channels realistically tops out in the low thousands/hour, while a
    /// genuinely viral TikTok can hit tens of thousands. Defaults match the old global constant so any
    /// caller that doesn't set this explicitly (e.g. ScoutAgent's cross-platform-matching pre-seed) keeps
    /// prior behavior.
    /// </summary>
    public double VelocityCapPerHour { get; set; } = 10000;

    /// <summary>Multiples of the platform baseline that counts as a "maxed out" (100%) outlier score.</summary>
    public double OutlierMultiplierCap { get; set; } = 20;

    /// <summary>Weighted final virality score (0-100)</summary>
    public double FinalScore => CalculateFinalScore();

    private double CalculateFinalScore()
    {
        // Weighted average of all signals. Velocity carries the most weight on purpose: a video with
        // 100K views in 2 hours should outrank one with 1M views over a month — OutlierFactor rewards raw
        // magnitude regardless of how long it took to get there, so velocity has to dominate for "fast
        // riser" trends to actually win over "big but slow" ones.
        const double wMomentum = 0.20;
        const double wVelocity = 0.35;
        const double wOutlier = 0.15;
        const double wEngagement = 0.15;
        const double wCrossPlatform = 0.15;

        var normalizedVelocity = Math.Min(ViewVelocity / Math.Max(VelocityCapPerHour, 1) * 100, 100);
        var normalizedOutlier = Math.Min(OutlierFactor / Math.Max(OutlierMultiplierCap, 1) * 100, 100);
        var normalizedEngagement = Math.Min(EngagementRate * 100 * 10, 100); // 10% engagement = 100
        var normalizedCrossPlatform = CrossPlatformCount / 3.0 * 100;

        return Math.Round(
            TrendMomentum * wMomentum +
            normalizedVelocity * wVelocity +
            normalizedOutlier * wOutlier +
            normalizedEngagement * wEngagement +
            normalizedCrossPlatform * wCrossPlatform,
            2);
    }

    public string GetLabel() => FinalScore switch
    {
        >= 80 => "🔥 Viral",
        >= 60 => "📈 High Potential",
        >= 40 => "⚡ Rising",
        >= 20 => "📊 Moderate",
        _ => "💤 Low"
    };
}
