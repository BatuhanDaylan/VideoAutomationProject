using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.AI;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Infrastructure.Scrapers;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Agents;

/// <summary>
/// Analyzer Agent: turns a Trend into a persisted Script + Video + Scenes via Gemini.
/// Lives in Infrastructure (not Application/Services/Agents), same reason as ScoutAgent — it needs
/// direct AppDbContext access and Infrastructure already references Application, so the reverse would
/// be a circular reference.
/// A Video row acts as the pipeline envelope (it already carries nullable TrendId/ScriptId from the
/// Faz 1 scaffold): analysis creates it with Status=ScriptGenerated, later phases fill in the rest.
/// </summary>
public class AnalyzerAgent : IAnalyzerAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IContentGenerator _contentGenerator;
    private readonly YouTubeTranscriptService _youTubeTranscriptService;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnalyzerAgent> _logger;

    public AnalyzerAgent(IContentGenerator contentGenerator, YouTubeTranscriptService youTubeTranscriptService, AppDbContext db, IConfiguration configuration, ILogger<AnalyzerAgent> logger)
    {
        _contentGenerator = contentGenerator;
        _youTubeTranscriptService = youTubeTranscriptService;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Script> AnalyzeAndGenerateScriptAsync(Trend trend, Template? template = null, CancellationToken ct = default)
    {
        var resolvedTemplate = template ?? await _db.Templates.FirstOrDefaultAsync(t => t.IsDefault, ct)
            ?? throw new AnalysisException("Varsayılan şablon bulunamadı (Templates tablosunda IsDefault=true kaydı yok).");

        var language = _configuration.GetValue("Pipeline:Language", "tr")!;
        var visualMode = ResolveVisualMode(null);

        // Both TikTok (costs real Apify credit per call) and YouTube (free, but its unofficial transcript
        // endpoint rate-limits/blocks an IP that fetches too many in a burst) transcripts are only fetched
        // here — once a trend is actually being analyzed (i.e. selected) — never during bulk scanning of
        // dozens/hundreds of candidates.
        // PRIMARY understanding for YouTube: have Gemini actually WATCH the video (multimodal, by URL — no
        // download). This is the fix for scripts that had nothing to do with the real video: ~99% of trends
        // have no transcript, so title-only understanding was guessing. Done here (analysis time, one trend),
        // never during bulk scanning.
        // Run the multimodal analysis when EITHER the summary (konu) OR the feasibility score (uygunluk) is
        // missing — not just when ContentSummary is empty. Otherwise a trend analyzed BEFORE feasibility was
        // wired in (has konu, no uygunluk) would skip this on re-analyze and its AiFeasibilityScore would
        // stay null forever. One multimodal call fills both.
        if (trend.SourcePlatform == Platform.YouTube
            && (string.IsNullOrWhiteSpace(trend.ContentSummary) || !trend.AiFeasibilityScore.HasValue))
        {
            var analysis = await _contentGenerator.AnalyzeYouTubeVideoAsync(trend.SourceUrl, ct);
            if (analysis != null)
            {
                trend.ContentSummary = analysis.Summary;
                trend.AiFeasibilityScore = analysis.FeasibilityScore;
                trend.AiFeasibilityReason = analysis.FeasibilityReason;

                // Persist konu + uygunluk IMMEDIATELY, before the (quota-prone) script generation below.
                // Otherwise a 429 on the script step throws and NOTHING is saved — the expensive multimodal
                // understanding would be discarded. This way the trend keeps its summary + feasibility (and
                // shows them in the UI) even if the script fails, and the user can retry generation later.
                await _db.SaveChangesAsync(ct);
            }
        }

        // Transcript is a YouTube-only fallback, fetched only when the multimodal video understanding didn't
        // come through (free Python fetch, no cost). TikTok/Instagram no longer fetch transcripts at all —
        // they're handled via the manual-description flow, so the PAID TikTok transcript Apify actor was
        // removed entirely.
        if (trend.SourcePlatform == Platform.YouTube
            && string.IsNullOrWhiteSpace(trend.Transcript)
            && string.IsNullOrWhiteSpace(trend.ContentSummary))
        {
            var videoId = trend.SourceUrl.Split("v=").LastOrDefault();
            if (!string.IsNullOrWhiteSpace(videoId))
                trend.Transcript = await _youTubeTranscriptService.TryFetchTranscriptAsync(videoId, ct);
        }

        var script = await _contentGenerator.GenerateScriptAsync(trend, resolvedTemplate, language, ct);

        // Persist trend.IsProcessed=true in the SAME SaveChanges as the new Video (the helper's save), so a
        // successful analysis atomically marks the trend done and materializes its Video.
        trend.IsProcessed = true;
        var video = await PersistVideoFromScriptAsync(
            script, resolvedTemplate, visualMode, trend.Title, trend.Description, trend.Id, ct);

        _logger.LogInformation("Trend {TrendId} analyzed → Video {VideoId} created with {SceneCount} scenes",
            trend.Id, video.Id, video.Scenes.Count);

        return script;
    }

    public async Task<Video> CreateVideoFromIdeaAsync(string idea, int? templateId = null, string? visualMode = null, CancellationToken ct = default)
    {
        var template = templateId.HasValue
            ? await _db.Templates.FindAsync(new object[] { templateId.Value }, ct)
                ?? throw new AnalysisException($"Şablon {templateId} bulunamadı.")
            : await _db.Templates.FirstOrDefaultAsync(t => t.IsDefault, ct)
                ?? throw new AnalysisException("Varsayılan şablon bulunamadı (Templates tablosunda IsDefault=true kaydı yok).");

        var language = _configuration.GetValue("Pipeline:Language", "tr")!;
        var mode = ResolveVisualMode(visualMode);

        var script = await _contentGenerator.GenerateScriptFromIdeaAsync(idea, template, language, ct);

        var video = await PersistVideoFromScriptAsync(
            script, template, mode, script.Topic, "Sohbetten üretildi", trendId: null, ct);

        _logger.LogInformation("Sohbet fikri → Video {VideoId} created with {SceneCount} scenes",
            video.Id, video.Scenes.Count);

        return video;
    }

    public async Task<Video> AnalyzeManualAsync(Trend trend, string description, int? templateId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new AnalysisException("Video açıklaması boş olamaz.");

        var template = templateId.HasValue
            ? await _db.Templates.FindAsync(new object[] { templateId.Value }, ct)
                ?? throw new AnalysisException($"Şablon {templateId} bulunamadı.")
            : await _db.Templates.FirstOrDefaultAsync(t => t.IsDefault, ct)
                ?? throw new AnalysisException("Varsayılan şablon bulunamadı (Templates tablosunda IsDefault=true kaydı yok).");

        var language = _configuration.GetValue("Pipeline:Language", "tr")!;
        var visualMode = ResolveVisualMode(null);

        var result = await _contentGenerator.AnalyzeManualDescriptionAsync(description, template, language, ct);

        // The user's own description IS the content understanding here; store it + the feasibility score
        // Gemini derived from it (this is the "skor o zaman belirlenir" moment for TikTok/IG, since the
        // automatic feasibility sweep only runs on YouTube).
        trend.ContentSummary = description;
        trend.AiFeasibilityScore = result.FeasibilityScore;
        trend.AiFeasibilityReason = result.FeasibilityReason;
        trend.IsProcessed = true;

        var video = await PersistVideoFromScriptAsync(
            result.Script, template, visualMode, trend.Title, trend.Description, trend.Id, ct);

        _logger.LogInformation("Manuel analiz: trend {TrendId} → Video {VideoId} (uygunluk={Score})",
            trend.Id, video.Id, result.FeasibilityScore);

        return video;
    }

    private VisualMode ResolveVisualMode(string? name)
    {
        var configured = string.IsNullOrWhiteSpace(name)
            ? _configuration.GetValue("Pipeline:DefaultVisualMode", "AIGenerated")!
            : name;
        return Enum.TryParse<VisualMode>(configured, ignoreCase: true, out var m) ? m : VisualMode.AIGenerated;
    }

    /// <summary>
    /// Turns a generated Script's RawJsonResponse into persisted Scenes + a Video envelope
    /// (Status=ScriptGenerated). Shared by the trend path (AnalyzeAndGenerateScriptAsync) and the manual
    /// idea/chat path (CreateVideoFromIdeaAsync) so both build the Video identically — only Title/Description/
    /// TrendId differ.
    /// </summary>
    private async Task<Video> PersistVideoFromScriptAsync(
        Script script, Template template, VisualMode visualMode,
        string title, string description, int? trendId, CancellationToken ct)
    {
        var sceneData = JsonSerializer.Deserialize<GeminiScriptResponse>(script.RawJsonResponse, JsonOptions)
            ?? throw new AnalysisException("Üretilen senaryonun sahne verisi ayrıştırılamadı.");

        // Prefer the title Gemini wrote for OUR video; fall back to the source (trend/idea) title if empty.
        var resolvedTitle = string.IsNullOrWhiteSpace(sceneData.Title) ? title : sceneData.Title.Trim();

        var scenes = sceneData.Scenes.Select((s, index) => new Scene
        {
            OrderIndex = index,
            NarrationText = s.NarrationText,
            SceneType = s.SceneType,
            VisualKeywordA = s.VisualKeywordA,
            VisualKeywordB = s.VisualKeywordB,
            VideoPrompt = s.VideoPrompt,
            DurationSeconds = s.DurationSeconds > 0 ? s.DurationSeconds : 5,
            TransitionEffect = template.TransitionStyle,
        }).ToList();

        var video = new Video
        {
            Title = resolvedTitle,
            Description = description,
            TemplateType = template.Type,
            VisualMode = visualMode,
            TrendId = trendId,
            Status = VideoStatus.ScriptGenerated,
            DurationSeconds = (int)scenes.Sum(s => s.DurationSeconds),
            Script = script,
            Scenes = scenes,
        };

        _db.Videos.Add(video);
        await _db.SaveChangesAsync(ct);

        return video;
    }
}
