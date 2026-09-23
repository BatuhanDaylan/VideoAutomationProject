using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Agents;

/// <summary>
/// Generator Agent: turns the Video row AnalyzerAgent created (Status=ScriptGenerated) into a real MP4.
/// Lives in Infrastructure (not Application/Services/Agents), same reason as ScoutAgent/AnalyzerAgent —
/// needs AppDbContext, and Infrastructure already references Application.
/// Does not create a new Video row — it continues the one Faz 3 left behind, advancing VideoStatus as it
/// goes (AssetsDownloading → AssetsReady → Composing → Composed, or Failed on error).
/// </summary>
public class GeneratorAgent : IGeneratorAgent
{
    private readonly IEnumerable<IStockVideoProvider> _visualProviders;
    private readonly IVideoComposer _composer;
    private readonly INotifier _notifier;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeneratorAgent> _logger;

    public GeneratorAgent(IEnumerable<IStockVideoProvider> visualProviders, IVideoComposer composer, INotifier notifier, AppDbContext db, IConfiguration configuration, ILogger<GeneratorAgent> logger)
    {
        _visualProviders = visualProviders;
        _composer = composer;
        _notifier = notifier;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Video> GenerateVideoAsync(Script script, Template template, CancellationToken ct = default, bool requestApproval = true)
    {
        var video = script.Video ?? await _db.Videos
            .Include(v => v.Scenes)
            .FirstOrDefaultAsync(v => v.ScriptId == script.Id, ct)
            ?? throw new VideoGenerationException($"Script {script.Id} için bağlı bir Video bulunamadı — önce analyze çağrılmalı.");

        try
        {
            var assetsDir = _configuration.GetValue("Pipeline:AssetsDirectory", "assets")!;
            var workDir = Path.Combine(assetsDir, "work", video.Id.ToString());

            video.Status = VideoStatus.AssetsDownloading;
            await _db.SaveChangesAsync(ct);

            foreach (var scene in video.Scenes.OrderBy(s => s.OrderIndex))
                await FetchSceneAssetsAsync(scene, video.VisualMode, template, workDir, ct);

            video.Status = VideoStatus.AssetsReady;
            await _db.SaveChangesAsync(ct);

            video.Status = VideoStatus.Composing;
            await _db.SaveChangesAsync(ct);

            await _composer.ComposeVideoAsync(video, template, ct);

            video.Status = VideoStatus.Composed;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Video {VideoId} generated: {Output} ({Duration}s)",
                video.Id, video.OutputFilePath, video.DurationSeconds);

            // requestApproval=false when the interactive Telegram conversation flow drives generation — it
            // sends the video + its own review buttons itself, so the simple approve/reject notifier must
            // not also fire (that would double-message and race the two flows).
            if (requestApproval)
            {
                try
                {
                    await _notifier.RequestApprovalAsync(video, ct);
                    video.Status = VideoStatus.PendingApproval;
                }
                catch (Exception ex)
                {
                    // Not configured / unreachable Telegram must not fail an otherwise-successful generation —
                    // the video just stays Composed, publishable manually via POST /api/videos/{id}/publish/{platform}.
                    _logger.LogWarning(ex, "Video {VideoId} onay için Telegram'a gönderilemedi, Composed durumunda bırakılıyor", video.Id);
                }
            }
            await _db.SaveChangesAsync(ct);

            return video;
        }
        catch (Exception ex)
        {
            video.Status = VideoStatus.Failed;
            await _db.SaveChangesAsync(ct);

            if (ex is VideoGenerationException) throw;
            throw new VideoGenerationException(ex.Message, ex);
        }
    }

    private async Task FetchSceneAssetsAsync(Scene scene, VisualMode visualMode, Template template, string workDir, CancellationToken ct)
    {
        var provider = SelectProvider(visualMode, scene.OrderIndex);
        var promptText = ResolvePromptText(provider, scene);

        if (template.UseABSplit)
        {
            var (pathA, pathB) = await provider.GetDualVideosAsync(promptText, workDir, ct);
            scene.VideoFilePathA = pathA;
            scene.VideoFilePathB = pathB;
        }
        else
        {
            scene.VideoFilePathA = await provider.GetSingleVideoAsync(promptText, workDir, ct);
            scene.VideoFilePathB = null; // clear any stale value from a previous run that had UseABSplit on
        }

        if (visualMode.UsesNativeAudio())
        {
            // The generated clip brings its own ambience/dialogue — narrating over it would talk across the
            // model's own audio. Null (not a stale path from an earlier narrated run) tells the composer to
            // use the clip's own audio track instead.
            scene.AudioFilePath = null;
            return;
        }

        var rawAudioPath = Path.Combine(workDir, $"scene-{scene.OrderIndex}-raw.mp3");
        var cleanAudioPath = Path.Combine(workDir, $"scene-{scene.OrderIndex}-audio.mp3");
        await _composer.GenerateTTSAsync(scene.NarrationText, template.TTSVoice, rawAudioPath, ct);
        await _composer.RemoveSilenceAsync(rawAudioPath, cleanAudioPath, ct);
        scene.AudioFilePath = cleanAudioPath;
    }

    /// <summary>
    /// Pexels needs a short search term (VisualKeywordA); an AI video-generation provider does far better
    /// with the richer, cinematic VideoPrompt Gemini wrote (Faz 3) — falls back to VisualKeywordA if that's
    /// empty (e.g. non-YouTube trends with no transcript to ground a detailed prompt in).
    /// </summary>
    private static string ResolvePromptText(IStockVideoProvider provider, Scene scene)
        => provider.SupportedMode == VisualMode.AIGenerated && !string.IsNullOrWhiteSpace(scene.VideoPrompt)
            ? scene.VideoPrompt
            : scene.VisualKeywordA;

    /// <summary>
    /// Mixed mode alternates providers by scene position (even → first registered provider, odd →
    /// second) since there's no per-scene "which visual source" signal from Faz 3 to key off of.
    /// </summary>
    private IStockVideoProvider SelectProvider(VisualMode visualMode, int sceneOrderIndex)
    {
        if (visualMode == VisualMode.Mixed)
        {
            var providers = _visualProviders.ToList();
            if (providers.Count == 0)
                throw new VideoGenerationException("Kayıtlı görsel sağlayıcı (IStockVideoProvider) yok.");
            return providers[sceneOrderIndex % providers.Count];
        }

        return _visualProviders.FirstOrDefault(p => p.SupportedMode == visualMode)
            ?? throw new VideoGenerationException($"'{visualMode}' için kayıtlı bir IStockVideoProvider yok.");
    }
}
