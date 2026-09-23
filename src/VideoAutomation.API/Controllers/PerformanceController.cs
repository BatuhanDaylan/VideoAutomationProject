using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Infrastructure.Publishing;

namespace VideoAutomation.API.Controllers;

/// <summary>
/// Performance feedback: for the current user's published YouTube videos, shows their latest public stats
/// (views/likes/comments) plus a small summary. Stats are captured daily by a Hangfire job; POST refresh
/// triggers a capture on demand. Closes the "publish and forget" gap so the user sees what actually landed.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PerformanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PublicationStatsRefresher _refresher;

    public PerformanceController(AppDbContext db, PublicationStatsRefresher refresher)
    {
        _db = db; _refresher = refresher;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        // Videos is user-filtered → this only sees the current user's published YouTube videos.
        // Flat-join form (result selector) instead of a filtered Select INSIDE SelectMany: the latter needs a
        // SQL APPLY/LATERAL, which SQLite doesn't support (live-hit: 500 "requires the SQL APPLY operation").
        // Also pull the SOURCE trend's predicted scores (virality + AI-feasibility) so the UI can show
        // predicted-vs-actual — the display half of the "learn what worked" loop. Video→Trend is optional
        // (idea/chat videos have none), so these are nullable.
        var rows = await _db.Videos
            .SelectMany(v => v.Publications, (v, p) => new { v, p })
            .Where(x => x.p.IsPublished && x.p.Platform == Platform.YouTube)
            .Select(x => new
            {
                PublicationId = x.p.Id,
                VideoId = x.v.Id,
                Title = x.v.Title,
                x.p.PlatformUrl,
                x.p.PublishedAt,
                Virality = x.v.Trend != null ? (double?)x.v.Trend.FinalViralityScore : null,
                Feasibility = x.v.Trend != null ? x.v.Trend.AiFeasibilityScore : null,
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
            return Ok(new { videos = Array.Empty<object>(), summary = new { count = 0, totalViews = 0L, avgViews = 0L } });

        var pubIds = rows.Select(r => r.PublicationId).ToList();
        // GroupBy+First doesn't translate reliably on SQLite → materialize then reduce in memory.
        var statsByPub = (await _db.PublicationStats
                .Where(s => pubIds.Contains(s.PublicationId))
                .ToListAsync(ct))
            .GroupBy(s => s.PublicationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());

        var videos = rows.Select(r =>
        {
            statsByPub.TryGetValue(r.PublicationId, out var s);
            return new
            {
                videoId = r.VideoId,
                title = r.Title,
                url = r.PlatformUrl,
                publishedAt = r.PublishedAt,
                viewCount = s?.ViewCount ?? 0,
                likeCount = s?.LikeCount ?? 0,
                commentCount = s?.CommentCount ?? 0,
                capturedAt = (DateTime?)s?.CreatedAt,
                viralityScore = r.Virality,
                feasibilityScore = r.Feasibility,
            };
        })
        .OrderByDescending(v => v.viewCount)
        .ToList();

        var totalViews = videos.Sum(v => v.viewCount);
        return Ok(new
        {
            videos,
            summary = new
            {
                count = videos.Count,
                totalViews,
                avgViews = videos.Count > 0 ? totalViews / videos.Count : 0,
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var written = await _refresher.RefreshAsync(ct);
        var emptyHint = "Public istatistik bulunamadı — videolar 'private' olabilir (API anahtarı private videoları göremez; YouTube'da unlisted/public yapınca sayılar gelir) ya da YouTube Data API anahtarı eksik.";
        return Ok(new { written, message = written > 0 ? $"{written} video istatistiği güncellendi." : emptyHint });
    }
}
