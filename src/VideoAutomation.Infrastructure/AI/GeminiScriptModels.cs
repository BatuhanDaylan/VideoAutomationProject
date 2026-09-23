namespace VideoAutomation.Infrastructure.AI;

/// <summary>
/// Shape of the JSON Gemini is asked to return for script generation. Shared between
/// GeminiContentGenerator (which requests and stores it) and AnalyzerAgent (which re-parses
/// Script.RawJsonResponse to build the Scene list) — internal since both live in Infrastructure.
/// </summary>
internal class GeminiScriptResponse
{
    // Punchy Turkish title Gemini writes for OUR video (not the source trend's title). Used as Video.Title,
    // which the YouTube upload publishes. Empty → caller falls back to the source trend's title.
    public string Title { get; set; } = string.Empty;
    public List<GeminiSceneDto> Scenes { get; set; } = new();
}

internal class GeminiSceneDto
{
    public string SceneType { get; set; } = string.Empty; // free-form label for this segment's role in the source content
    public string NarrationText { get; set; } = string.Empty;
    public string VisualKeywordA { get; set; } = string.Empty;
    public string VisualKeywordB { get; set; } = string.Empty;
    public string VideoPrompt { get; set; } = string.Empty; // detailed English prompt for AI video generation (Kling)
    public double DurationSeconds { get; set; }
}
