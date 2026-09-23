namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Chat message for the AI Chat Interface (SignalR-based).
/// </summary>
public class ChatMessage : BaseEntity
{
    /// <summary>Owning user — every query is scoped to this via an AppDbContext global filter.</summary>
    public int UserId { get; set; }

    public string Role { get; set; } = "user"; // user, assistant, system
    public string Content { get; set; } = string.Empty;
    public string? ActionType { get; set; } // scout, analyze, generate, publish, null
    public string? ActionResult { get; set; } // JSON result of the action
    public string SessionId { get; set; } = string.Empty;
}
