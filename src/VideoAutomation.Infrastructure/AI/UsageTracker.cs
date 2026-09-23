using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.Infrastructure.AI;

/// <summary>
/// Best-effort per-user/day API call counter (see <see cref="Domain.Entities.ApiUsage"/>). Used where a
/// provider has no usage-read API (Gemini free tier). Increments with a single atomic SQL UPSERT so it
/// (a) doesn't race under concurrent calls and (b) does NOT flush the DbContext's change tracker — calling
/// SaveChanges here would prematurely persist unrelated tracked entities mid-pipeline. Never throws:
/// a counter failure must never break the actual API call it's counting.
/// </summary>
public class UsageTracker
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserId _currentUser;
    private readonly ILogger<UsageTracker> _logger;

    public UsageTracker(AppDbContext db, ICurrentUserId currentUser, ILogger<UsageTracker> logger)
    {
        _db = db; _currentUser = currentUser; _logger = logger;
    }

    public async Task TrackAsync(string provider, CancellationToken ct = default)
    {
        if (_currentUser.Value is not { } uid) return;
        var day = DateTime.UtcNow.Date;
        var now = DateTime.UtcNow;
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ApiUsages (UserId, Provider, Day, Count, CreatedAt)
                VALUES ({uid}, {provider}, {day}, 1, {now})
                ON CONFLICT(UserId, Provider, Day) DO UPDATE SET Count = Count + 1;", ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Kullanım sayacı güncellenemedi (provider={Provider}) — yok sayılıyor.", provider);
        }
    }
}
