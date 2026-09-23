using Microsoft.AspNetCore.Mvc;
using VideoAutomation.Application.Interfaces;

namespace VideoAutomation.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(IConfiguration config, IUserSecrets secrets, ILogger<SettingsController> logger)
    {
        _config = config;
        _secrets = secrets;
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        // Per-user: reports the LOGGED-IN user's configured keys (from IUserSecrets), not global config.
        var geminiCount = _secrets.GeminiApiKeys.Count;
        var status = new
        {
            geminiConfigured = geminiCount > 0,
            geminiKeyCount = geminiCount,
            geminiKeysAvailable = geminiCount,
            pexelsConfigured = !string.IsNullOrEmpty(_secrets.PexelsKey),
            apifyConfigured = !string.IsNullOrEmpty(_secrets.ApifyToken),
            aiVideoConfigured = !string.IsNullOrEmpty(_secrets.AiVideoToken),
            telegramConfigured = !string.IsNullOrEmpty(_secrets.TelegramBotToken) && !string.IsNullOrEmpty(_secrets.TelegramChatId),
            youtubeConfigured = !string.IsNullOrEmpty(_secrets.YouTubeClientId),
            region = _config["Pipeline:Region"] ?? "TR",
            language = _config["Pipeline:Language"] ?? "tr",
        };

        return Ok(status);
    }

    [HttpGet("pipeline-config")]
    public IActionResult GetPipelineConfig()
    {
        return Ok(new
        {
            region = _config["Pipeline:Region"],
            language = _config["Pipeline:Language"],
            maxTrendsPerRun = int.Parse(_config["Pipeline:MaxTrendsPerRun"] ?? "5"),
            defaultVisualMode = _config["Pipeline:DefaultVisualMode"],
            defaultTemplateId = int.Parse(_config["Pipeline:DefaultTemplateId"] ?? "1"),
            cronSchedule = _config["Pipeline:CronSchedule"],
            videoWidth = int.Parse(_config["Video:Width"] ?? "1080"),
            videoHeight = int.Parse(_config["Video:Height"] ?? "1920"),
            targetDuration = int.Parse(_config["Video:TargetDurationSeconds"] ?? "60"),
        });
    }
}
