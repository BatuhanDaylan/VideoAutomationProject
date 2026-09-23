using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Represents a generated video with its metadata, status, and related scenes.
/// </summary>
public class Video : BaseEntity
{
    /// <summary>Owning user — every query is scoped to this via an AppDbContext global filter.</summary>
    public int UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty; // JSON array
    public VideoStatus Status { get; set; } = VideoStatus.Pending;
    public VisualMode VisualMode { get; set; } = VisualMode.StockVideo;
    public TemplateType TemplateType { get; set; } = TemplateType.Documentary;

    // File paths
    public string? OutputFilePath { get; set; }
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// The Telegram message carrying the approve/reject inline keyboard for this video, so the polling
    /// service can edit it in place (remove the buttons, show the outcome) once a decision comes in.
    /// </summary>
    public long? TelegramMessageId { get; set; }

    // Video properties
    public int DurationSeconds { get; set; }
    public int Width { get; set; } = 1080;
    public int Height { get; set; } = 1920;

    // Source trend
    public int? TrendId { get; set; }
    public Trend? Trend { get; set; }

    // Related script
    public int? ScriptId { get; set; }
    public Script? Script { get; set; }

    // Navigation
    public ICollection<Scene> Scenes { get; set; } = new List<Scene>();
    public ICollection<Publication> Publications { get; set; } = new List<Publication>();
}
