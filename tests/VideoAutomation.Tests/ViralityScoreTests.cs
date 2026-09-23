using VideoAutomation.Domain.ValueObjects;

namespace VideoAutomation.Tests;

/// <summary>
/// ViralityScore.FinalScore is a fixed weighted average (velocity 0.35, momentum 0.20, outlier/engagement/
/// cross-platform 0.15 each) of five normalized signals. These pin the arithmetic and the label bands so a
/// future re-weighting can't silently drift the scores the whole pipeline ranks on.
/// </summary>
public class ViralityScoreTests
{
    [Fact]
    public void AllSignalsZero_ScoresZero()
    {
        var score = new ViralityScore();
        Assert.Equal(0, score.FinalScore);
    }

    [Fact]
    public void AllSignalsMaxed_ScoresExactly100()
    {
        // Every normalized component hits its 100 ceiling → weighted sum of weights (which total 1.0) = 100.
        var score = new ViralityScore
        {
            TrendMomentum = 100,
            ViewVelocity = 10000,     // == default VelocityCapPerHour → normalized 100
            OutlierFactor = 20,       // == default OutlierMultiplierCap → normalized 100
            EngagementRate = 0.10,    // 0.10 * 1000 = 100
            CrossPlatformCount = 3,   // 3/3 → 100
        };
        Assert.Equal(100, score.FinalScore);
    }

    [Fact]
    public void VelocityAboveCap_IsClampedTo100()
    {
        // 5x the cap must not push the velocity component past 100; only velocity is set, weight 0.35 → 35.
        var score = new ViralityScore { ViewVelocity = 50000 };
        Assert.Equal(35, score.FinalScore);
    }

    [Fact]
    public void CustomVelocityCap_ChangesNormalization()
    {
        // Platform-tuned cap: 1500 of 3000 = half → 50 * 0.35 = 17.5.
        var score = new ViralityScore { VelocityCapPerHour = 3000, ViewVelocity = 1500 };
        Assert.Equal(17.5, score.FinalScore);
    }

    [Fact]
    public void VelocityOutweighsRawMagnitude()
    {
        // A fast riser (100K views in ~2h) should outrank a big-but-slow video of equal engagement.
        var fastRiser = new ViralityScore { ViewVelocity = 10000, OutlierFactor = 2 };
        var bigButSlow = new ViralityScore { ViewVelocity = 500, OutlierFactor = 20 };
        Assert.True(fastRiser.FinalScore > bigButSlow.FinalScore);
    }

    [Theory]
    [InlineData(0, 0, 0, "💤 Low")]              // 0
    [InlineData(0, 0, 3, "💤 Low")]              // cross-only: 15 → Low
    [InlineData(0, 10000, 0, "📊 Moderate")]     // velocity-only: 35 → Moderate
    [InlineData(100, 10000, 0, "⚡ Rising")]     // 20 + 35 = 55 → Rising
    [InlineData(100, 10000, 3, "📈 High Potential")] // 20 + 35 + 15 = 70 → High Potential
    public void GetLabel_MatchesScoreBand(double momentum, double velocity, int crossPlatform, string expected)
    {
        var score = new ViralityScore
        {
            TrendMomentum = momentum,
            ViewVelocity = velocity,
            CrossPlatformCount = crossPlatform,
        };
        Assert.Equal(expected, score.GetLabel());
    }

    [Fact]
    public void MaxedSignals_LabeledViral()
    {
        var score = new ViralityScore
        {
            TrendMomentum = 100, ViewVelocity = 10000, OutlierFactor = 20,
            EngagementRate = 0.10, CrossPlatformCount = 3,
        };
        Assert.Equal("🔥 Viral", score.GetLabel());
    }
}
