using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.Agents;

namespace VideoAutomation.Tests;

/// <summary>
/// ScoutAgent's cross-platform matching estimates how many platforms cover the same real-world trend via
/// normalized title/keyword token overlap (Jaccard), since there's no shared ID space across
/// YouTube/TikTok/Instagram. Tests hit the pure, config-free internal helpers directly.
/// </summary>
public class CrossPlatformMatchingTests
{
    [Fact]
    public void Jaccard_IdenticalSets_IsOne()
    {
        var a = new HashSet<string> { "kedi", "kutu" };
        var b = new HashSet<string> { "kedi", "kutu" };
        Assert.Equal(1.0, ScoutAgent.JaccardSimilarity(a, b));
    }

    [Fact]
    public void Jaccard_DisjointSets_IsZero()
    {
        Assert.Equal(0.0, ScoutAgent.JaccardSimilarity(new() { "kedi" }, new() { "araba" }));
    }

    [Fact]
    public void Jaccard_PartialOverlap_IsIntersectionOverUnion()
    {
        // {a,b} ∩ {b,c} = 1, ∪ = 3 → 1/3
        var sim = ScoutAgent.JaccardSimilarity(new() { "a", "b" }, new() { "b", "c" });
        Assert.Equal(1.0 / 3.0, sim, precision: 10);
    }

    [Fact]
    public void Jaccard_EmptySet_IsZero()
    {
        Assert.Equal(0.0, ScoutAgent.JaccardSimilarity(new(), new() { "kedi" }));
    }

    [Fact]
    public void SameTopicOnTwoPlatforms_CountsBoth()
    {
        var trends = new List<Trend>
        {
            Trend("kediler kutu sevgisi", Platform.YouTube),
            Trend("kediler kutu sevgisi", Platform.TikTok),
        };

        ScoutAgent.ApplyCrossPlatformMatching(trends, threshold: 0.3);

        Assert.Equal(2, trends[0].ViralityScore!.CrossPlatformCount);
        Assert.Equal(2, trends[1].ViralityScore!.CrossPlatformCount);
    }

    [Fact]
    public void SamePlatform_DoesNotCountAsCrossPlatform()
    {
        // Two identical titles but both YouTube: the inner loop skips same-platform trends, so each stays 1.
        var trends = new List<Trend>
        {
            Trend("kediler kutu sevgisi", Platform.YouTube),
            Trend("kediler kutu sevgisi", Platform.YouTube),
        };

        ScoutAgent.ApplyCrossPlatformMatching(trends, threshold: 0.3);

        Assert.Equal(1, trends[0].ViralityScore!.CrossPlatformCount);
        Assert.Equal(1, trends[1].ViralityScore!.CrossPlatformCount);
    }

    [Fact]
    public void SameTopicOnAllThreePlatforms_CountsThree()
    {
        var trends = new List<Trend>
        {
            Trend("kediler kutu sevgisi", Platform.YouTube),
            Trend("kediler kutu sevgisi", Platform.Instagram),
            Trend("kediler kutu sevgisi", Platform.TikTok),
        };

        ScoutAgent.ApplyCrossPlatformMatching(trends, threshold: 0.3);

        Assert.All(trends, t => Assert.Equal(3, t.ViralityScore!.CrossPlatformCount));
    }

    [Fact]
    public void DifferentTopics_StayAtOne()
    {
        var trends = new List<Trend>
        {
            Trend("kediler kutu sevgisi", Platform.YouTube),
            Trend("araba yaris pisti", Platform.TikTok),
        };

        ScoutAgent.ApplyCrossPlatformMatching(trends, threshold: 0.3);

        Assert.Equal(1, trends[0].ViralityScore!.CrossPlatformCount);
        Assert.Equal(1, trends[1].ViralityScore!.CrossPlatformCount);
    }

    private static Trend Trend(string title, Platform platform) => new()
    {
        Title = title,
        SourcePlatform = platform,
        SourceUrl = $"https://example.com/{platform}/{title.GetHashCode()}",
    };
}
