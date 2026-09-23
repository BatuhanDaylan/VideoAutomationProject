namespace VideoAutomation.Domain.Entities;

/// <summary>
/// Represents a single scene within a video. Each scene has narration text,
/// two visual keywords for A/B split, and associated media file paths.
/// </summary>
public class Scene : BaseEntity
{
    public int VideoId { get; set; }
    public Video Video { get; set; } = null!;

    public int OrderIndex { get; set; } // Scene order in the video
    public string NarrationText { get; set; } = string.Empty;
    public string SceneType { get; set; } = "normal"; // free-form label for this segment's role in the source content

    // A/B Split visual keywords for Pexels search
    public string VisualKeywordA { get; set; } = string.Empty;
    public string VisualKeywordB { get; set; } = string.Empty;

    // Richer, cinematic description for AI video generation (Kling/aimlapi) — Pexels keeps using the
    // short VisualKeywordA/B above since it needs search terms, not a paragraph.
    public string VideoPrompt { get; set; } = string.Empty;

    // Generated asset paths
    public string? AudioFilePath { get; set; }      // TTS output
    public string? VideoFilePathA { get; set; }     // Pexels/HF visual A
    public string? VideoFilePathB { get; set; }     // Pexels/HF visual B
    public string? ComposedFilePath { get; set; }   // Final composed scene

    // Timing
    public double DurationSeconds { get; set; }
    public bool IsAvatarScene { get; set; } // Avatar injection target

    // Transition to next scene
    public string TransitionEffect { get; set; } = "fade"; // fade, slide, wipe, dissolve
}
