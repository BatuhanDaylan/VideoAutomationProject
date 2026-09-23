using Hangfire;
using Microsoft.AspNetCore.Mvc;
using VideoAutomation.Infrastructure.Publishing;

namespace VideoAutomation.API.Controllers;

/// <summary>
/// Manual trigger for the autonomous Telegram candidate flow — the same batch the peak-hour recurring jobs
/// run, but on demand (for testing, or "send me candidates now"). Enqueued through Hangfire so the HTTP
/// request returns immediately while the scan + Telegram sends happen in the background.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TelegramController : ControllerBase
{
    private readonly IBackgroundJobClient _jobs;
    private readonly IConfiguration _config;

    public TelegramController(IBackgroundJobClient jobs, IConfiguration config)
    {
        _jobs = jobs; _config = config;
    }

    [HttpPost("batch-now")]
    public IActionResult BatchNow()
    {
        // Manual trigger → short window (Telegram:ManualWindowHours, default 3h) so you get the freshest trends.
        var window = _config.GetValue("Telegram:ManualWindowHours", 3);
        var jobId = _jobs.Enqueue<TelegramConversationService>(s => s.RunAutonomousBatchAsync(window, CancellationToken.None));
        return Accepted(new { message = $"Aday partisi kuyruğa alındı (son {window} saat) — Telegram'a birazdan gelecek.", hangfireJobId = jobId });
    }
}
