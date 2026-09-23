using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Tracks one interactive Telegram video-creation conversation for a chat. One active session per chat at
/// a time (a new peak-hour batch replaces the previous). Persisted so the flow survives an app restart.
/// </summary>
public class TelegramSession : BaseEntity
{
    /// <summary>Owning user — every query is scoped to this via an AppDbContext global filter.</summary>
    public int UserId { get; set; }

    public string ChatId { get; set; } = string.Empty;
    public TelegramFlowStage Stage { get; set; } = TelegramFlowStage.AwaitingSelection;

    /// <summary>JSON array of candidate Trend ids sent in this batch.</summary>
    public string CandidateTrendIds { get; set; } = "[]";

    public int? SelectedTrendId { get; set; }
    public int? VideoId { get; set; }

    /// <summary>Chosen generation mode once the user picks a tool ("StockVideo" / "AIGenerated").</summary>
    public string? VisualMode { get; set; }

    /// <summary>Only one session per chat drives the flow; older ones are deactivated.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The publish-time window (hours) this batch searched — so the "farklı videolar öner" refresh
    /// searches the same window. Manual triggers use a short window (3h); peak-hour jobs use a wider one
    /// covering the gap since the previous batch (12:00→15h, 19:00→7h, 21:00→2h).</summary>
    public int WithinHours { get; set; } = 3;
}
