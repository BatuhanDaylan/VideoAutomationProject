namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Represents a pipeline execution run with status tracking.
/// </summary>
public class PipelineRun : BaseEntity
{
    /// <summary>Owning user — every query is scoped to this via an AppDbContext global filter.</summary>
    public int UserId { get; set; }

    public string Status { get; set; } = "pending"; // pending, running, completed, failed
    public string CurrentStep { get; set; } = string.Empty;
    public int TotalSteps { get; set; }
    public int CompletedSteps { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }
    public string LogOutput { get; set; } = string.Empty;

    // Related video
    public int? VideoId { get; set; }
    public Video? Video { get; set; }
}
