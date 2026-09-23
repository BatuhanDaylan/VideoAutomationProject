namespace VideoAutomation.Domain.Entities;

/// <summary>
/// A future-scheduled full pipeline run (Tarama→Analiz→Üretim→Yayın) for the content calendar. A Hangfire
/// delayed job fires at ScheduledAtUtc and runs the same PipelineOrchestrator the manual/peak-hour paths use.
/// Status is a string (same convention as PipelineRun): Pending → Running → Completed/Failed, or Canceled.
/// </summary>
public class ScheduledRun : BaseEntity
{
    public int UserId { get; set; }

    public DateTime ScheduledAtUtc { get; set; }
    public string Region { get; set; } = "TR";

    public string Status { get; set; } = "Pending";
    public string? HangfireJobId { get; set; }   // kept so a cancel can delete the delayed job

    public int? ResultPipelineRunId { get; set; }
    public int? ResultVideoId { get; set; }
    public string? ErrorMessage { get; set; }
}
