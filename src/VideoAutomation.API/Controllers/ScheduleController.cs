using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Infrastructure.Agents;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.API.Controllers;

/// <summary>
/// Content calendar: schedule the FULL pipeline (Tarama→Analiz→Üretim→Yayın) to run at a future time. Each
/// schedule enqueues a Hangfire delayed job that runs as the owning user; canceling deletes that job.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ScheduleController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<ScheduleController> _logger;

    public ScheduleController(AppDbContext db, IBackgroundJobClient jobs, ILogger<ScheduleController> logger)
    {
        _db = db; _jobs = jobs; _logger = logger;
    }

    // Plain class (not a positional record) — same [FromBody] binding lesson as the other controllers.
    public class CreateRequest
    {
        public DateTime ScheduledAt { get; set; } // ISO string from the client (UTC, e.g. via Date.toISOString())
        public string? Region { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var rows = await _db.ScheduledRuns
            .OrderByDescending(s => s.ScheduledAtUtc)
            .Take(100)
            .ToListAsync(ct);
        return Ok(rows.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (req == null) return BadRequest(new { error = "Boş istek." });
        var whenUtc = req.ScheduledAt.ToUniversalTime();
        if (whenUtc <= DateTime.UtcNow.AddMinutes(1))
            return BadRequest(new { error = "Zaman en az 1 dakika ileride olmalı." });

        var sr = new ScheduledRun { ScheduledAtUtc = whenUtc, Region = string.IsNullOrWhiteSpace(req.Region) ? "TR" : req.Region!.Trim(), Status = "Pending" };
        _db.ScheduledRuns.Add(sr);
        await _db.SaveChangesAsync(ct);  // need sr.Id before scheduling the job

        var jobId = _jobs.Schedule<ScheduleRunnerService>(r => r.ExecuteAsync(sr.Id, CancellationToken.None), new DateTimeOffset(whenUtc, TimeSpan.Zero));
        sr.HangfireJobId = jobId;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Zamanlanmış çalıştırma {Id} oluşturuldu: {When:o} (Hangfire {Job})", sr.Id, whenUtc, jobId);
        return Ok(Map(sr));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var sr = await _db.ScheduledRuns.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sr == null) return NotFound();
        if (sr.Status != "Pending")
            return BadRequest(new { error = $"Yalnızca bekleyen çalıştırmalar iptal edilebilir (mevcut durum: {sr.Status})." });

        if (!string.IsNullOrWhiteSpace(sr.HangfireJobId))
            BackgroundJob.Delete(sr.HangfireJobId);
        sr.Status = "Canceled";
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Zamanlanmış çalıştırma {Id} iptal edildi.", id);
        return Ok(Map(sr));
    }

    private static object Map(ScheduledRun s) => new
    {
        id = s.Id,
        scheduledAt = s.ScheduledAtUtc,
        region = s.Region,
        status = s.Status,
        resultVideoId = s.ResultVideoId,
        errorMessage = s.ErrorMessage,
        createdAt = s.CreatedAt,
    };
}
