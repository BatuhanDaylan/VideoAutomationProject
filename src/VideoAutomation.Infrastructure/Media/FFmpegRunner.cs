using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Shared.Exceptions;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace VideoAutomation.Infrastructure.Media;

/// <summary>
/// Low-level FFmpeg process runner shared by every Media/ service (FFmpegVideoComposer's scene/final
/// composition, thumbnail extraction). Same "low-level client + higher-level services"
/// layering as GeminiClient (Faz 3) — callers hand it a fully-formed raw argument string (all -i inputs,
/// filter_complex, mapping, output path already included) since the filter graphs here are too specific
/// to model through Xabe's fluent builder.
/// </summary>
public class FFmpegRunner
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _initialized;

    private readonly IConfiguration _configuration;
    private readonly ILogger<FFmpegRunner> _logger;

    public FFmpegRunner(IConfiguration configuration, ILogger<FFmpegRunner> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Downloads the FFmpeg/FFprobe binaries into assets/ffmpeg on first use (Xabe.FFmpeg skips the
    /// download if they're already present there from a previous run). Guarded so concurrent requests
    /// don't race to download in parallel.
    /// </summary>
    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await InitLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var assetsDir = _configuration.GetValue("Pipeline:AssetsDirectory", "assets")!;
            var ffmpegDir = Path.Combine(assetsDir, "ffmpeg");
            Directory.CreateDirectory(ffmpegDir);
            FFmpeg.SetExecutablesPath(ffmpegDir);

            var alreadyPresent = File.Exists(Path.Combine(ffmpegDir, "ffmpeg.exe")) || File.Exists(Path.Combine(ffmpegDir, "ffmpeg"));
            if (!alreadyPresent)
            {
                _logger.LogInformation("FFmpeg binaries not found in {Dir}, downloading (first run only)...", ffmpegDir);
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Full, ffmpegDir);
                _logger.LogInformation("✅ FFmpeg binaries ready in {Dir}", ffmpegDir);
            }

            _initialized = true;
        }
        catch (Exception ex)
        {
            throw new VideoGenerationException($"FFmpeg binary'leri hazırlanamadı: {ex.Message}", ex);
        }
        finally
        {
            InitLock.Release();
        }
    }

    /// <summary>
    /// Runs ffmpeg with a fully-formed raw argument string (must already include -i, filters, output
    /// path — but NOT -y/-n: SetOverwriteOutput below adds Xabe's own -y, and supplying both throws
    /// "both -y and -n supplied").
    /// </summary>
    public async Task RunAsync(string arguments, CancellationToken ct)
    {
        await EnsureReadyAsync(ct);

        try
        {
            var conversion = FFmpeg.Conversions.New();
            conversion.SetOverwriteOutput(true);
            conversion.AddParameter(arguments);
            await conversion.Start(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg command failed. Arguments: {Arguments}", arguments);
            throw new VideoGenerationException($"FFmpeg komutu başarısız: {ex.Message}", ex);
        }
    }

    public async Task<TimeSpan> GetDurationAsync(string mediaPath, CancellationToken ct)
    {
        await EnsureReadyAsync(ct);

        try
        {
            var info = await FFmpeg.GetMediaInfo(mediaPath, ct);
            return info.Duration;
        }
        catch (Exception ex)
        {
            throw new VideoGenerationException($"Medya süresi okunamadı ({mediaPath}): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Whether the file carries at least one audio stream. Needed on the AI-generated path: those clips are
    /// supposed to come with native audio (ambience/dialogue), but a model or provider that returns a silent
    /// clip would otherwise make a later "-map 0:a" fail with a confusing stream-not-found error — callers
    /// substitute a generated silent track instead.
    /// </summary>
    public async Task<bool> HasAudioStreamAsync(string mediaPath, CancellationToken ct)
    {
        await EnsureReadyAsync(ct);

        try
        {
            var info = await FFmpeg.GetMediaInfo(mediaPath, ct);
            return info.AudioStreams.Any();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ses akışı kontrolü başarısız ({Path}), sessiz varsayılıyor", mediaPath);
            return false;
        }
    }
}
