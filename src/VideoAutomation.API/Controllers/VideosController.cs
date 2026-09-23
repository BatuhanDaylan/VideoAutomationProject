using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Application.DTOs;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;

namespace VideoAutomation.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IGeneratorAgent _generatorAgent;
    private readonly IPublisherAgent _publisherAgent;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<VideosController> _logger;

    public VideosController(AppDbContext db, IGeneratorAgent generatorAgent, IPublisherAgent publisherAgent, IWebHostEnvironment env, ILogger<VideosController> logger)
    {
        _db = db;
        _generatorAgent = generatorAgent;
        _publisherAgent = publisherAgent;
        _env = env;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<VideoDto>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Materialize first, then map: enum ToString() and nested DTO construction aren't reliably
        // SQL-translatable — same fix applied to TrendsController.GetAll in Faz 2.
        var videos = await _db.Videos
            .Include(v => v.Script)
            .Include(v => v.Scenes.OrderBy(s => s.OrderIndex))
            .Include(v => v.Publications)
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(videos.Select(MapToDto).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VideoDto>> GetById(int id)
    {
        var video = await _db.Videos
            .Include(v => v.Script)
            .Include(v => v.Scenes.OrderBy(s => s.OrderIndex))
            .Include(v => v.Publications)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (video == null) return NotFound();

        return Ok(MapToDto(video));
    }

    [HttpPost("{id}/generate")]
    public async Task<ActionResult<VideoDto>> Generate(int id, [FromQuery] string? visualMode, CancellationToken ct = default)
    {
        var video = await _db.Videos
            .Include(v => v.Script)
            .Include(v => v.Scenes)
            .Include(v => v.Publications)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (video == null) return NotFound();
        if (video.Script == null) return BadRequest(new { error = "Bu video için önce POST /api/trends/{id}/analyze çağrılmalı." });

        if (!string.IsNullOrWhiteSpace(visualMode))
        {
            if (!Enum.TryParse<VisualMode>(visualMode, ignoreCase: true, out var parsedMode))
                return BadRequest(new { error = $"Geçersiz visualMode: '{visualMode}'. Beklenen: StockVideo, AIGenerated, Mixed." });
            video.VisualMode = parsedMode;
        }

        var template = await _db.Templates.FirstOrDefaultAsync(t => t.Type == video.TemplateType, ct)
            ?? await _db.Templates.FirstOrDefaultAsync(t => t.IsDefault, ct);
        if (template == null) return StatusCode(500, new { error = "Şablon bulunamadı." });

        var result = await _generatorAgent.GenerateVideoAsync(video.Script, template, ct);

        _logger.LogInformation("Video {Id} generate tamamlandı: {Output}", id, result.OutputFilePath);

        return Ok(MapToDto(result));
    }

    // Plain class, not a positional record (same [FromBody] binding lesson as ChatController).
    public class SceneUpdateRequest
    {
        public string? NarrationText { get; set; }
        public string? VideoPrompt { get; set; }
        public string? VisualKeywordA { get; set; }
        public string? VisualKeywordB { get; set; }
    }

    /// <summary>
    /// Edits a scene's generation inputs BEFORE the video is produced — the AI-video prompt (VideoPrompt),
    /// the narration, and the Pexels keywords. Lets the user review/tweak exactly what each scene will be
    /// generated from. Only null-check: a provided field overwrites, an omitted (null) field is left as-is.
    /// Changes take effect on the next POST /api/videos/{id}/generate (they don't alter an already-composed mp4).
    /// </summary>
    [HttpPut("{videoId}/scenes/{sceneId}")]
    public async Task<ActionResult<VideoDto>> UpdateScene(int videoId, int sceneId, [FromBody] SceneUpdateRequest request, CancellationToken ct = default)
    {
        if (request == null) return BadRequest(new { error = "Boş istek." });

        var scene = await _db.Scenes.FirstOrDefaultAsync(s => s.Id == sceneId && s.VideoId == videoId, ct);
        if (scene == null) return NotFound();

        if (request.NarrationText != null) scene.NarrationText = request.NarrationText;
        if (request.VideoPrompt != null) scene.VideoPrompt = request.VideoPrompt;
        if (request.VisualKeywordA != null) scene.VisualKeywordA = request.VisualKeywordA;
        if (request.VisualKeywordB != null) scene.VisualKeywordB = request.VisualKeywordB;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Sahne {SceneId} (video {VideoId}) güncellendi", sceneId, videoId);

        var video = await _db.Videos
            .Include(v => v.Script)
            .Include(v => v.Scenes.OrderBy(s => s.OrderIndex))
            .Include(v => v.Publications)
            .FirstAsync(v => v.Id == videoId, ct);
        return Ok(MapToDto(video));
    }

    /// <summary>
    /// Manual/fallback publish path — bypasses the Telegram approval flow entirely. Exists because the
    /// approval flow is best-effort (GeneratorAgent leaves a video at Composed instead of PendingApproval
    /// if Telegram isn't reachable/configured), and because testing the pipeline shouldn't require a live
    /// Telegram round-trip every time.
    /// </summary>
    [HttpPost("{id}/publish/{platform}")]
    public async Task<ActionResult<PublicationDto>> Publish(int id, Platform platform, CancellationToken ct = default)
    {
        var video = await _db.Videos.FindAsync([id], ct);
        if (video == null) return NotFound();

        var publication = await _publisherAgent.PublishToPlatformAsync(video, platform, ct);
        return Ok(new PublicationDto
        {
            Id = publication.Id,
            Platform = publication.Platform.ToString(),
            IsPublished = publication.IsPublished,
            PublishedAt = publication.PublishedAt,
            PlatformUrl = publication.PlatformUrl,
            ErrorMessage = publication.ErrorMessage,
        });
    }

    /// <summary>
    /// Streams the composed mp4 so the dashboard's &lt;video&gt; element can preview it. enableRangeProcessing
    /// lets the browser seek/scrub instead of pulling the whole file up front. The path comes from the DB
    /// (Video.OutputFilePath, set by GeneratorAgent), never from user input, so there's no traversal surface;
    /// stored relative (e.g. "output\10.mp4"), resolved against the content root the same way GeneratorAgent
    /// created it (cwd == content root when running via `dotnet run`).
    /// </summary>
    [HttpGet("{id}/file")]
    public async Task<IActionResult> GetFile(int id, CancellationToken ct = default)
    {
        var video = await _db.Videos.FindAsync([id], ct);
        if (video == null) return NotFound();
        if (string.IsNullOrWhiteSpace(video.OutputFilePath))
            return NotFound(new { error = "Bu video henüz üretilmedi (OutputFilePath boş)." });

        var fullPath = Path.IsPathRooted(video.OutputFilePath)
            ? video.OutputFilePath
            : Path.Combine(_env.ContentRootPath, video.OutputFilePath);

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { error = $"Video dosyası diskte bulunamadı: {video.OutputFilePath}" });

        return PhysicalFile(fullPath, "video/mp4", enableRangeProcessing: true);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        // Delete in two ordered steps. FK graph: Scenes/Publications → Video are ON DELETE CASCADE (so the
        // DB removes them automatically when the Video row goes), but Video → Script is NO ACTION and it's a
        // one-to-one, so a single SaveChanges could try to delete the Script before the Video (EF can't always
        // infer the order for one-to-one) → "FOREIGN KEY constraint failed". Removing the Video first (which
        // cascades its Scenes/Publications and clears the last reference to the Script), then the Script,
        // satisfies every constraint regardless of EF's inference.
        var video = await _db.Videos
            .Include(v => v.Script)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (video == null) return NotFound();

        var outputPath = video.OutputFilePath;
        var script = video.Script;

        // A PipelineRun records which video it produced (PipelineRuns.VideoId → Videos, NO ACTION), which
        // blocks deleting that video. Null the reference first so the run history survives but no longer
        // pins the video. (Scenes/Publications are ON DELETE CASCADE, so those go automatically.)
        var runs = await _db.PipelineRuns.Where(r => r.VideoId == id).ToListAsync();
        if (runs.Count > 0)
        {
            foreach (var r in runs) r.VideoId = null;
            await _db.SaveChangesAsync();
        }

        _db.Videos.Remove(video);
        await _db.SaveChangesAsync();

        if (script != null)
        {
            _db.Scripts.Remove(script);
            await _db.SaveChangesAsync();
        }

        // Best-effort: drop the rendered mp4 too so deleted videos don't leave orphan files on disk.
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            try
            {
                var fullPath = Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(_env.ContentRootPath, outputPath);
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Video {Id} kaydı silindi ama çıktı dosyası silinemedi: {Path}", id, outputPath);
            }
        }

        _logger.LogInformation("Video deleted: {Id} - {Title}", video.Id, video.Title);
        return NoContent();
    }

    // internal (not private): TrendsController's analyze endpoint reuses this to map the Video
    // created by AnalyzerAgent, so both controllers return videos in the same shape.
    internal static VideoDto MapToDto(Video video) => new()
    {
        Id = video.Id,
        Title = video.Title,
        Description = video.Description,
        Status = video.Status.ToString(),
        VisualMode = video.VisualMode.ToString(),
        TemplateType = video.TemplateType.ToString(),
        DurationSeconds = video.DurationSeconds,
        OutputFilePath = video.OutputFilePath,
        ThumbnailPath = video.ThumbnailPath,
        TrendId = video.TrendId,
        CreatedAt = video.CreatedAt,
        Script = video.Script == null ? null : new ScriptDto
        {
            Id = video.Script.Id,
            Topic = video.Script.Topic,
            FullText = video.Script.FullText,
            Language = video.Script.Language,
        },
        Scenes = video.Scenes.Select(s => new SceneDto
        {
            Id = s.Id,
            OrderIndex = s.OrderIndex,
            NarrationText = s.NarrationText,
            SceneType = s.SceneType,
            VisualKeywordA = s.VisualKeywordA,
            VisualKeywordB = s.VisualKeywordB,
            VideoPrompt = s.VideoPrompt,
            DurationSeconds = s.DurationSeconds,
            IsAvatarScene = s.IsAvatarScene,
            TransitionEffect = s.TransitionEffect
        }).OrderBy(s => s.OrderIndex).ToList(),
        Publications = video.Publications.Select(p => new PublicationDto
        {
            Id = p.Id,
            Platform = p.Platform.ToString(),
            IsPublished = p.IsPublished,
            PublishedAt = p.PublishedAt,
            PlatformUrl = p.PlatformUrl,
            ErrorMessage = p.ErrorMessage
        }).ToList()
    };
}
