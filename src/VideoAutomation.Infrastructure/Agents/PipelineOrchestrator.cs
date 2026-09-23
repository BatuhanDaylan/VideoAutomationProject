using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Agents;

/// <summary>
/// Orchestrates Tarama (ScoutAgent.DiscoverTrendsAsync) → Seçim (ScoutAgent.SelectBestFeasibleTrendsAsync)
/// → Analiz (AnalyzerAgent) → Üretim (GeneratorAgent) as one PipelineRun-tracked unit. GeneratorAgent's own
/// Telegram approval step (Faz 5) is what keeps a human in the loop before anything reaches YouTube — this
/// orchestrator does not publish anything itself, it stops once a video is Composed/PendingApproval/Failed.
/// Lives in Infrastructure (not Application/Services/Agents), same reason as every other agent here —
/// needs direct AppDbContext access for PipelineRun bookkeeping.
/// </summary>
public class PipelineOrchestrator : IPipelineOrchestrator
{
    private readonly IScoutAgent _scoutAgent;
    private readonly IAnalyzerAgent _analyzerAgent;
    private readonly IGeneratorAgent _generatorAgent;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public PipelineOrchestrator(
        IScoutAgent scoutAgent, IAnalyzerAgent analyzerAgent, IGeneratorAgent generatorAgent,
        AppDbContext db, IConfiguration configuration, ILogger<PipelineOrchestrator> logger)
    {
        _scoutAgent = scoutAgent;
        _analyzerAgent = analyzerAgent;
        _generatorAgent = generatorAgent;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PipelineRun> RunAsync(string region = "TR", CancellationToken ct = default)
    {
        var run = new PipelineRun
        {
            Status = "running",
            TotalSteps = 4,
            CurrentStep = "Tarama",
            StartedAt = DateTime.UtcNow,
        };
        _db.PipelineRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        try
        {
            // YouTube-only on purpose: the automatic pipeline only produces YouTube videos (multimodal
            // understanding is free via URL), so scraping TikTok/Instagram here would just burn Apify credit
            // on the daily cron for trends the auto path never selects. TikTok/IG are scraped only via the
            // manual "Tara" and handled manually.
            var scanned = await RetryAsync(() => _scoutAgent.DiscoverTrendsAsync(region, new[] { Platform.YouTube }, ct), "Tarama", ct);
            AppendLog(run, $"Tarama (yalnızca YouTube): {scanned.Count} trend bulundu.");
            await AdvanceAsync(run, "Seçim", ct);

            var maxTrends = _configuration.GetValue("Pipeline:MaxTrendsPerRun", 5);
            var candidates = await RetryAsync(() => _scoutAgent.SelectBestFeasibleTrendsAsync(maxTrends, false, ct), "Seçim", ct);
            var trend = candidates.FirstOrDefault();
            if (trend == null)
            {
                await FailAsync(run, "Üretilebilir/uygun (Türkçe + AI ile üretilebilir) trend bulunamadı.");
                return run;
            }
            AppendLog(run, $"Seçilen trend: [{trend.Id}] {trend.Title} (viral={trend.FinalViralityScore:F1}, ai={trend.AiFeasibilityScore})");
            await AdvanceAsync(run, "Analiz", ct);

            // Passing null lets AnalyzerAgent resolve the default Template itself — same as the manual
            // POST /api/trends/{id}/analyze path with no templateId. The resulting Video.TemplateType is
            // re-resolved into an actual Template row below, the same lookup VideosController.Generate uses,
            // so GeneratorAgent gets a template consistent with what AnalyzerAgent actually picked.
            var script = await RetryAsync(() => _analyzerAgent.AnalyzeAndGenerateScriptAsync(trend, null, ct), "Analiz", ct);
            if (script.Video == null)
            {
                await FailAsync(run, "Senaryo üretildi ama bir Video'ya bağlanamadı.");
                return run;
            }
            run.VideoId = script.Video.Id;
            AppendLog(run, $"Senaryo üretildi: video {script.Video.Id} ({script.Video.Scenes.Count} sahne).");
            await AdvanceAsync(run, "Üretim", ct);

            var template = await _db.Templates.FirstOrDefaultAsync(t => t.Type == script.Video.TemplateType, ct)
                ?? await _db.Templates.FirstOrDefaultAsync(t => t.IsDefault, ct)
                ?? throw new VideoGenerationException("Şablon bulunamadı (Templates tablosunda uygun kayıt yok).");

            var video = await RetryAsync(() => _generatorAgent.GenerateVideoAsync(script, template, ct), "Üretim", ct);
            AppendLog(run, $"Üretim tamamlandı: video {video.Id}, durum={video.Status}.");

            run.Status = "completed";
            run.CompletedSteps = run.TotalSteps;
            run.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Pipeline run {RunId} tamamlandı: video {VideoId}, durum={Status}", run.Id, video.Id, video.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline run {RunId} adım '{Step}' üzerinde başarısız oldu", run.Id, run.CurrentStep);
            await FailAsync(run, ex.Message);
        }

        return run;
    }

    /// <summary>
    /// Retries ONLY genuinely-transient, connection-layer failures (HTTP/socket/non-caller-cancelled
    /// timeout) — 2 retries, exponential backoff. Deliberately does NOT retry this project's own domain
    /// exceptions on their OWN account (ScrapingException/AnalysisException/VideoGenerationException/
    /// PublishingException): those are thrown after the lower layer already diagnosed a specific, usually
    /// permanent cause (a missing token, a Gemini safety block, no eligible trend) — blindly retrying would
    /// just repeat the same failure while burning API quota, not recover from it.
    /// BUT every agent in this codebase wraps a caught network exception as `new XException(msg, ex)` —
    /// preserving the original as InnerException rather than losing it in a string. So a Gemini HTTP
    /// timeout surfaces here as AnalysisException → InvalidOperationException → TaskCanceledException, not
    /// as a bare TaskCanceledException (live-confirmed: a top-level-type-only Polly policy let a real
    /// 120s Gemini timeout through with ZERO retries, single attempt, no backoff). IsTransient walks the
    /// full InnerException chain instead of matching only the outermost type.
    /// </summary>
    private Task<T> RetryAsync<T>(Func<Task<T>> action, string stepName, CancellationToken ct)
    {
        var policy = Policy
            .Handle<Exception>(ex => IsTransient(ex, ct))
            .WaitAndRetryAsync(2, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex, "{Step} adımı geçici bir hatayla karşılaştı, {Delay}sn sonra {Attempt}. deneme yapılıyor",
                        stepName, delay.TotalSeconds, attempt));

        return policy.ExecuteAsync(action);
    }

    private static bool IsTransient(Exception ex, CancellationToken ct)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException or System.Net.Sockets.SocketException)
                return true;
            if (current is TaskCanceledException && !ct.IsCancellationRequested)
                return true;
        }
        return false;
    }

    private async Task AdvanceAsync(PipelineRun run, string nextStep, CancellationToken ct)
    {
        run.CompletedSteps++;
        run.CurrentStep = nextStep;
        await _db.SaveChangesAsync(ct);
    }

    private async Task FailAsync(PipelineRun run, string error)
    {
        run.Status = "failed";
        run.ErrorMessage = error;
        run.CompletedAt = DateTime.UtcNow;
        AppendLog(run, $"HATA: {error}");
        // CancellationToken.None, deliberately: if the failure IS the caller's token being cancelled, saving
        // with that same token would throw again immediately and the run would be stuck at "running"
        // forever with no error recorded — same class of bug fixed in PublisherAgent (Faz 5).
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private static void AppendLog(PipelineRun run, string line) =>
        run.LogOutput += $"[{DateTime.UtcNow:HH:mm:ss}] {line}\n";
}
