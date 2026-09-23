using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAutomation.Application.DTOs;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TrendsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IScoutAgent _scoutAgent;
    private readonly IAnalyzerAgent _analyzerAgent;
    private readonly ILogger<TrendsController> _logger;

    public TrendsController(AppDbContext db, IScoutAgent scoutAgent, IAnalyzerAgent analyzerAgent, ILogger<TrendsController> logger)
    {
        _db = db;
        _scoutAgent = scoutAgent;
        _analyzerAgent = analyzerAgent;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<TrendDto>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int recentHours = 24)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Trends.AsQueryable();

        // "Current trends only" = filter primarily by when the VIDEO was PUBLISHED, not when we scraped it
        // (scrape time made "son 1 saat" show days-old YouTube videos just because a rescan refreshed the row).
        // YouTube always has PublishedAt so it uses real publish time; TikTok/Instagram often don't, so they
        // fall back to CreatedAt (first-seen) — the best available signal — instead of being hidden entirely.
        // recentHours <= 0 means "no window — show everything".
        if (recentHours > 0)
        {
            var cutoff = DateTime.UtcNow.AddHours(-recentHours);
            query = query.Where(t => (t.PublishedAt ?? t.CreatedAt) >= cutoff);
        }

        // Materialize first, then map: Tags (JSON string) parsing and ViralityScore.GetLabel() can't be
        // translated to SQL by the SQLite provider — projecting them inside a Select() throws at runtime.
        var trends = await query
            .OrderByDescending(t => t.FinalViralityScore)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(trends.Select(MapToDto).ToList());
    }

    [HttpGet("top")]
    public async Task<ActionResult<List<TrendDto>>> GetTop([FromQuery] int topN = 5, [FromQuery] string region = "TR", CancellationToken ct = default)
    {
        var trends = await _scoutAgent.GetTopViralTrendsAsync(topN, region, ct);
        return Ok(trends.Select(MapToDto).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TrendDto>> GetById(int id)
    {
        var trend = await _db.Trends.FindAsync(id);
        if (trend == null) return NotFound();

        return Ok(MapToDto(trend));
    }

    [HttpPost("{id}/select")]
    public async Task<IActionResult> SelectTrend(int id)
    {
        var trend = await _db.Trends.FindAsync(id);
        if (trend == null) return NotFound();

        trend.IsSelected = true;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Trend selected for video generation: {Title} (Score: {Score})",
            trend.Title, trend.FinalViralityScore);

        return Ok(new { message = "Trend selected for video generation" });
    }

    [HttpPost("scan")]
    public async Task<ActionResult<ScanResultDto>> Scan([FromQuery] string region = "TR", CancellationToken ct = default)
    {
        // Manual scan: all platforms (null) — this is the on-demand path where the user explicitly wants
        // fresh TikTok/Instagram options (Apify spent here, not on the automatic cron).
        var trends = await _scoutAgent.DiscoverTrendsAsync(region, platforms: null, ct);

        var result = new ScanResultDto
        {
            TotalTrends = trends.Count,
            ByPlatform = trends
                .GroupBy(t => t.SourcePlatform.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
            TopTrends = trends
                .OrderByDescending(t => t.FinalViralityScore)
                .Take(10)
                .Select(MapToDto)
                .ToList(),
            ScannedAt = DateTime.UtcNow,
        };

        _logger.LogInformation("Trend scan complete: {Total} trends across {Platforms} platforms",
            result.TotalTrends, result.ByPlatform.Count);

        return Ok(result);
    }

    /// <summary>
    /// Runs the AI-feasibility check over the most viral unprocessed trends and returns them re-ranked by
    /// how well they can actually be recreated as an AI-generated video — the highest-scoring viral trend
    /// is useless if it hinges on a real person's face or real event footage that a text-to-video model
    /// can't reproduce. Thin wrapper: the actual selection logic lives in IScoutAgent.SelectBestFeasibleTrendsAsync
    /// (shared with PipelineOrchestrator's automatic run, so there's one definition of "worth producing").
    /// </summary>
    [HttpPost("evaluate-feasibility")]
    public async Task<ActionResult<List<TrendDto>>> EvaluateFeasibility(
        [FromQuery] int topN = 10, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        var ranked = await _scoutAgent.SelectBestFeasibleTrendsAsync(topN, force, ct);
        return Ok(ranked.Select(MapToDto).ToList());
    }

    // Plain class, not a positional record: System.Text.Json binds [FromBody] payloads far more predictably
    // this way (same lesson as ChatController).
    public class ManualAnalyzeRequest
    {
        public string Description { get; set; } = string.Empty;
        public int? TemplateId { get; set; }
    }

    /// <summary>
    /// Manual analysis for TikTok/Instagram trends (the automatic pipeline is YouTube-only): the user
    /// watched the video and describes what it's about; Gemini derives the AI-feasibility score AND the
    /// scene script from that description in one call, and a Video is created — same as the auto path.
    /// </summary>
    [HttpPost("{id}/analyze-manual")]
    public async Task<ActionResult<VideoDto>> AnalyzeManual(int id, [FromBody] ManualAnalyzeRequest request, CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { error = "Video açıklaması (description) boş olamaz." });

        var trend = await _db.Trends.FindAsync(new object[] { id }, ct);
        if (trend == null) return NotFound();

        var video = await _analyzerAgent.AnalyzeManualAsync(trend, request.Description, request.TemplateId, ct);
        _logger.LogInformation("Trend {Id} manuel analiz edildi → video {VideoId}", id, video.Id);

        return Ok(VideosController.MapToDto(video));
    }

    [HttpPost("{id}/analyze")]
    public async Task<ActionResult<VideoDto>> Analyze(int id, [FromQuery] int? templateId, CancellationToken ct = default)
    {
        var trend = await _db.Trends.FindAsync(new object[] { id }, ct);
        if (trend == null) return NotFound();

        Template? template = null;
        if (templateId.HasValue)
        {
            template = await _db.Templates.FindAsync(new object[] { templateId.Value }, ct);
            if (template == null) return NotFound($"Template {templateId} not found");
        }

        var script = await _analyzerAgent.AnalyzeAndGenerateScriptAsync(trend, template, ct);

        // AnalyzerAgent builds Video.Scenes in memory and saves in the same DbContext, so EF Core's
        // relationship fixup populates this navigation without an extra query.
        if (script.Video == null)
            return StatusCode(500, new { error = "Script generated but no Video was linked to it." });

        _logger.LogInformation("Trend {Id} analyzed, script generated: {Topic}", id, script.Topic);

        return Ok(VideosController.MapToDto(script.Video));
    }

    private TrendDto MapToDto(Trend t)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(t.Tags))
        {
            try
            {
                tags = JsonSerializer.Deserialize<List<string>>(t.Tags) ?? new List<string>();
            }
            catch (JsonException)
            {
                _logger.LogWarning("Trend {Id} has malformed Tags JSON, returning empty list", t.Id);
            }
        }

        return new TrendDto
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            SourceUrl = t.SourceUrl,
            SourcePlatform = t.SourcePlatform.ToString(),
            Category = t.Category.ToString(),
            ViewCount = t.ViewCount,
            LikeCount = t.LikeCount,
            CommentCount = t.CommentCount,
            FinalViralityScore = t.FinalViralityScore,
            ViralityLabel = t.ViralityScore?.GetLabel() ?? "N/A",
            Tags = tags,
            IsSelected = t.IsSelected,
            CreatedAt = t.CreatedAt,
            PublishedAt = t.PublishedAt,
            LanguageCode = t.LanguageCode,
            AiFeasibilityScore = t.AiFeasibilityScore,
            AiFeasibilityReason = t.AiFeasibilityReason,
            ContentSummary = t.ContentSummary,
        };
    }
}
