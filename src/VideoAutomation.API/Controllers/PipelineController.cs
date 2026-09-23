using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAutomation.Application.DTOs;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.API.Controllers;

/// <summary>
/// Manual entry point into the same PipelineOrchestrator the Hangfire recurring job calls — both paths
/// enqueue through Hangfire so a manual run gets the same retry/dashboard/history behavior as the
/// scheduled one, and the HTTP request doesn't block for the run's full multi-minute duration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PipelineController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<PipelineController> _logger;

    public PipelineController(AppDbContext db, IBackgroundJobClient backgroundJobClient, ILogger<PipelineController> logger)
    {
        _db = db;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    [HttpPost("run-now")]
    public IActionResult RunNow([FromQuery] string region = "TR")
    {
        var jobId = _backgroundJobClient.Enqueue<IPipelineOrchestrator>(orchestrator => orchestrator.RunAsync(region, CancellationToken.None));
        _logger.LogInformation("Pipeline manuel olarak kuyruğa alındı (Hangfire job {JobId})", jobId);
        return Accepted(new { message = "Pipeline çalıştırması kuyruğa alındı, ilerlemesi GET /api/pipeline/runs üzerinden izlenebilir.", hangfireJobId = jobId });
    }

    [HttpGet("runs")]
    public async Task<ActionResult<List<PipelineStatusDto>>> GetRuns([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var runs = await _db.PipelineRuns
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(runs.Select(MapToDto).ToList());
    }

    [HttpGet("runs/{id}")]
    public async Task<ActionResult<PipelineStatusDto>> GetRun(int id, CancellationToken ct = default)
    {
        var run = await _db.PipelineRuns.FindAsync([id], ct);
        if (run == null) return NotFound();
        return Ok(MapToDto(run));
    }

    private static PipelineStatusDto MapToDto(PipelineRun r) => new()
    {
        Id = r.Id,
        Status = r.Status,
        CurrentStep = r.CurrentStep,
        TotalSteps = r.TotalSteps,
        CompletedSteps = r.CompletedSteps,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        ErrorMessage = r.ErrorMessage,
        LogOutput = r.LogOutput,
        VideoId = r.VideoId,
    };
}
