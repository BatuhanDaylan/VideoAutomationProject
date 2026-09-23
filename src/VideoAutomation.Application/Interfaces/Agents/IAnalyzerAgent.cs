using VideoAutomation.Domain.Entities;

namespace VideoAutomation.Application.Interfaces.Agents;

/// <summary>
/// Analyzer Agent: Analyzes trends and generates video scripts with Gemini AI.
/// </summary>
public interface IAnalyzerAgent
{
    Task<Script> AnalyzeAndGenerateScriptAsync(Trend trend, Template? template = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a persisted Video (Status=ScriptGenerated) + Scenes directly from a free-text idea or chat
    /// transcript — no Trend involved (TrendId stays null). Powers the manual "chat → video" page; the
    /// resulting Video is produced by GeneratorAgent exactly like a trend-sourced one.
    /// </summary>
    Task<Video> CreateVideoFromIdeaAsync(string idea, int? templateId = null, string? visualMode = null, CancellationToken ct = default);

    /// <summary>
    /// Manual analysis for a TikTok/Instagram trend: the user supplies a description of what the video is
    /// about (since we don't auto-understand those platforms), and Gemini derives both the AI-feasibility
    /// score and the scene Script from it in one call. Stores the description as the trend's ContentSummary
    /// and the score on the trend, then materializes a Video the same way the automatic path does.
    /// </summary>
    Task<Video> AnalyzeManualAsync(Trend trend, string description, int? templateId = null, CancellationToken ct = default);
}
