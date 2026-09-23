using VideoAutomation.Domain.Enums;
using VideoAutomation.Domain.ValueObjects;

namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Represents a trending topic discovered from YouTube, TikTok, or Instagram.
/// </summary>
public class Trend : BaseEntity
{
    /// <summary>Owning user — every query is scoped to this via an AppDbContext global filter.</summary>
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public Platform SourcePlatform { get; set; }
    public TrendCategory Category { get; set; } = TrendCategory.Other;
    public DateTime? PublishedAt { get; set; } // When the source content was published (platform-reported)
    public string? Transcript { get; set; } // YouTube-only: fetched via youtube-transcript-api, feeds Gemini prompts

    /// <summary>
    /// What a multimodal model (Gemini) ACTUALLY saw when it watched the source video — visuals, actions,
    /// people, audio/speech, the point/joke. Populated at analysis time (AnalyzerAgent), not scraping.
    /// This is the real content understanding a short-form video's title/description/transcript can't give:
    /// most trends have no transcript, so a title-only guess ("#keşfet #komedi") produced scripts unrelated
    /// to the actual video. Script generation uses this as its PRIMARY content source when present.
    /// </summary>
    public string? ContentSummary { get; set; }

    /// <summary>
    /// BCP-47-ish language code the platform itself reported (e.g. "tr", "en-US"), when it reports one at
    /// all — only YouTube does. Null means "platform didn't say", NOT "unknown language": ScoutAgent's
    /// language filter falls back to text heuristics in that case rather than dropping the candidate.
    /// </summary>
    public string? LanguageCode { get; set; }

    /// <summary>
    /// 0-100: how well this trend's content can be RECREATED as an AI-generated video (generic scenes and
    /// narration score high; content that hinges on a specific real person's face, real event footage, a
    /// specific song/dance, or game capture scores low, because a text-to-video model cannot reproduce it).
    /// Null until <see cref="AiFeasibilityReason"/>'s Gemini evaluation has actually been run for this trend.
    /// </summary>
    public int? AiFeasibilityScore { get; set; }
    public string? AiFeasibilityReason { get; set; }

    // Metrics from the source platform
    public long ViewCount { get; set; }
    public long LikeCount { get; set; }
    public long CommentCount { get; set; }
    public long ShareCount { get; set; }

    // Tags and keywords
    public string Tags { get; set; } = string.Empty; // JSON array of tags
    public string Keywords { get; set; } = string.Empty; // Comma-separated keywords

    // Virality scoring
    public ViralityScore? ViralityScore { get; set; }
    public double FinalViralityScore { get; set; }

    // Processing state
    public bool IsProcessed { get; set; }
    public bool IsSelected { get; set; } // Selected for video generation

    // Navigation
    public ICollection<Video> Videos { get; set; } = new List<Video>();
}
