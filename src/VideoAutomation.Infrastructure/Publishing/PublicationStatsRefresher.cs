using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.Infrastructure.Publishing;

/// <summary>
/// Captures a fresh public-stats snapshot for the active user's published YouTube videos. Invoked daily by a
/// Hangfire recurring job (and on demand via the Performance controller). Runs in the active user's scope, so
/// the query filter + IUserSecrets already resolve to that user — no Override needed (same model as the
/// autonomous Telegram batch, which also runs for the active user).
/// </summary>
public class PublicationStatsRefresher
{
    private readonly AppDbContext _db;
    private readonly YouTubeStatsService _stats;
    private readonly ILogger<PublicationStatsRefresher> _logger;

    public PublicationStatsRefresher(AppDbContext db, YouTubeStatsService stats, ILogger<PublicationStatsRefresher> logger)
    {
        _db = db; _stats = stats; _logger = logger;
    }

    /// <summary>Writes one PublicationStat row per published YouTube video whose current stats we can fetch.
    /// Returns how many snapshots were written.</summary>
    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        // Videos is user-filtered, so SelectMany scopes publications to the current/active user automatically.
        var pubs = await _db.Videos
            .SelectMany(v => v.Publications)
            .Where(p => p.IsPublished && p.Platform == Platform.YouTube && p.PlatformVideoId != null)
            .Select(p => new { p.Id, p.PlatformVideoId })
            .ToListAsync(ct);
        if (pubs.Count == 0) return 0;

        var stats = await _stats.GetStatsAsync(pubs.Select(p => p.PlatformVideoId!).ToList(), ct);
        if (stats.Count == 0) return 0;

        var written = 0;
        foreach (var p in pubs)
        {
            if (!stats.TryGetValue(p.PlatformVideoId!, out var s)) continue;
            _db.PublicationStats.Add(new PublicationStat
            {
                PublicationId = p.Id,
                ViewCount = s.ViewCount,
                LikeCount = s.LikeCount,
                CommentCount = s.CommentCount,
            });
            written++;
        }

        if (written > 0) await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Yayın istatistikleri yenilendi: {Count} video snapshot'ı yazıldı.", written);
        return written;
    }
}
