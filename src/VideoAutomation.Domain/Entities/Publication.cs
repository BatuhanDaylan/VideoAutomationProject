using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Represents a video publication to a specific platform (YouTube, Instagram, TikTok).
/// </summary>
public class Publication : BaseEntity
{
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;

    public Platform Platform { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }

    // Platform-specific IDs
    public string? PlatformVideoId { get; set; } // e.g., YouTube video ID
    public string? PlatformUrl { get; set; }

    // Customized metadata per platform
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;

    // Error tracking
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
}
