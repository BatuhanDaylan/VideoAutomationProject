namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Represents the AI-generated script for a video, structured as Hook → Context → Mechanism → Twist.
/// </summary>
public class Script : BaseEntity
{
    public string Topic { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public string Language { get; set; } = "tr"; // tr, en
    public string GeminiPromptUsed { get; set; } = string.Empty;
    public string RawJsonResponse { get; set; } = string.Empty;

    // Navigation
    public Video? Video { get; set; }
}
