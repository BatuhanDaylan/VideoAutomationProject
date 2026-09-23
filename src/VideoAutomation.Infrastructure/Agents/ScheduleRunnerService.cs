using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.Infrastructure.Agents;

/// <summary>
/// Fires when a scheduled content-calendar slot comes due (Hangfire delayed job). Runs the SAME full pipeline
/// as the manual/peak-hour paths (IPipelineOrchestrator) in the owning user's scope, and records the outcome
/// back on the ScheduledRun. No new pipeline logic — this is just the "run it later, as this user" wrapper.
/// </summary>
public class ScheduleRunnerService
{
    private readonly AppDbContext _db;
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly ICurrentUserId _currentUser;
    private readonly ILogger<ScheduleRunnerService> _logger;

    public ScheduleRunnerService(AppDbContext db, IPipelineOrchestrator orchestrator, ICurrentUserId currentUser, ILogger<ScheduleRunnerService> logger)
    {
        _db = db; _orchestrator = orchestrator; _currentUser = currentUser; _logger = logger;
    }

    public async Task ExecuteAsync(int scheduledRunId, CancellationToken ct)
    {
        // No logged-in user in a background job → find the row without the user filter, then pin its owner so
        // the pipeline runs with the right keys + data scope (same pattern as the Telegram flow).
        var sr = await _db.ScheduledRuns.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == scheduledRunId, ct);
        if (sr == null) return;
        if (sr.Status is "Canceled" or "Completed") return;  // canceled after enqueue, or a duplicate fire
        _currentUser.Override(sr.UserId);

        sr.Status = "Running";
        await _db.SaveChangesAsync(ct);

        try
        {
            var run = await _orchestrator.RunAsync(sr.Region, ct);
            sr.ResultPipelineRunId = run.Id;
            sr.ResultVideoId = run.VideoId;
            sr.Status = run.Status == "completed" ? "Completed" : "Failed";
            if (sr.Status == "Failed") sr.ErrorMessage = run.ErrorMessage;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Zamanlanmış çalıştırma {Id} bitti: {Status} (PipelineRun {RunId})", sr.Id, sr.Status, run.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zamanlanmış çalıştırma {Id} başarısız", sr.Id);
            sr.Status = "Failed";
            sr.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync(CancellationToken.None);  // iptal-güvenli: token iptal olsa da kayıt yazılsın
        }
    }
}
