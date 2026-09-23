using VideoAutomation.Domain.Entities;

namespace VideoAutomation.Application.Interfaces;

/// <summary>
/// How reproducible a trend's content is as an AI-generated video.
/// </summary>
/// <param name="Score">0-100. Higher means a text-to-video model can plausibly recreate the content.</param>
/// <param name="Reason">Short Turkish explanation of the score, surfaced to the user in the API response.</param>
public record AiFeasibilityResult(int Score, string Reason);

/// <summary>One conversation turn for the manual chat page. Role is "user" or "assistant".</summary>
public record ChatTurn(string Role, string Content);

/// <summary>
/// Result of manually analyzing a user-written description of a (TikTok/Instagram) video: an AI-feasibility
/// score + reason AND the adapted scene Script, produced in ONE Gemini call.
/// </summary>
public record ManualAnalysisResult(int FeasibilityScore, string FeasibilityReason, Script Script);

/// <summary>
/// Result of a multimodal YouTube analysis (Gemini watches the video): a Turkish content summary (what
/// actually happens) AND the AI-feasibility score + reason — both from the ONE call that watches the video.
/// </summary>
public record YouTubeAnalysis(string Summary, int FeasibilityScore, string FeasibilityReason);

public interface IContentGenerator
{
    Task<Script> GenerateScriptAsync(Trend trend, Template template, string language = "tr", CancellationToken ct = default);
    Task<string> AnalyzeTrendAsync(Trend trend, CancellationToken ct = default);

    /// <summary>
    /// Has a multimodal model actually WATCH a YouTube video (by URL) and, in ONE call, return both a rich
    /// Turkish description of what really happens (visuals, actions, people, audio, the point/joke) AND the
    /// AI-feasibility score + reason (judged directly from the video — better than judging from a text
    /// summary). This is the content-understanding a short-form video's title/transcript can't give. Returns
    /// null on failure — optional enrichment, never fatal; the caller falls back to transcript/title.
    /// </summary>
    Task<YouTubeAnalysis?> AnalyzeYouTubeVideoAsync(string youtubeUrl, CancellationToken ct = default);

    /// <summary>
    /// Free-form multi-turn chat with Gemini for the manual "shape a video idea by chatting" page — the
    /// model acts as a creative short-video assistant, not the trend-adaptation persona GenerateScriptAsync
    /// uses. Stateless: the caller passes the full history each time and gets back the model's next reply.
    /// </summary>
    Task<string> GenerateChatReplyAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct = default);

    /// <summary>
    /// Turns a free-text idea (or a whole chat transcript) directly into a scene-based Script — the same
    /// output shape GenerateScriptAsync produces from a Trend, so the rest of the pipeline (AnalyzerAgent's
    /// scene persistence, GeneratorAgent) consumes it unchanged. No Trend involved.
    /// </summary>
    Task<Script> GenerateScriptFromIdeaAsync(string idea, Template template, string language = "tr", CancellationToken ct = default);

    /// <summary>
    /// For manually-handled platforms (TikTok/Instagram): the user watched the video and wrote what it's
    /// about; from THAT description, in one call, Gemini both scores AI-feasibility (0-100 + reason) and
    /// produces the adapted scene Script. The score is only knowable here (the automatic feasibility sweep
    /// can't run on TikTok/IG since we don't understand their content automatically).
    /// </summary>
    Task<ManualAnalysisResult> AnalyzeManualDescriptionAsync(string description, Template template, string language = "tr", CancellationToken ct = default);

    /// <summary>
    /// Judges whether each trend's content could actually be recreated with AI-generated video clips plus
    /// TTS narration — the pipeline's only output format. Content that hinges on a specific real person's
    /// face, real event footage, a particular song/dance, or game capture cannot be, no matter how viral it
    /// is, so it's worth knowing BEFORE spending script-generation and video-generation budget on it.
    /// Takes the whole batch in one call rather than one call per trend: Gemini's free tier allows only 5
    /// requests/minute, which a per-trend loop exhausts immediately (live-confirmed 429s at 12 candidates).
    /// </summary>
    /// <returns>Map of Trend.Id to its result. Trends the model didn't return a verdict for are absent.</returns>
    Task<IReadOnlyDictionary<int, AiFeasibilityResult>> EvaluateAiFeasibilityAsync(
        IReadOnlyList<Trend> trends, CancellationToken ct = default);
}
