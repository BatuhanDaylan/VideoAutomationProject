using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VideoAutomation.Infrastructure.Scrapers;

/// <summary>
/// Shells out to the Python `youtube-transcript-api` CLI — YouTube Data API v3's own captions.download
/// only works with OAuth from the video's owner, so it's useless for scraping third-party trending
/// videos. This library hits YouTube's internal timedtext endpoint instead (no API key/OAuth needed),
/// same "practical unofficial access via Python subprocess" pattern as EdgeTtsService.
/// This is a best-effort enrichment, not a hard requirement: most trending videos won't have a fetchable
/// transcript (disabled, wrong language, etc.) — callers should treat a null result as normal, not a
/// scraping failure.
/// </summary>
public class YouTubeTranscriptService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<YouTubeTranscriptService> _logger;

    public YouTubeTranscriptService(IConfiguration configuration, ILogger<YouTubeTranscriptService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string?> TryFetchTranscriptAsync(string videoId, CancellationToken ct = default)
    {
        var pythonExe = _configuration.GetValue("Scraping:YouTubeTranscript:PythonExecutable", "python")!;
        var languages = _configuration.GetSection("Scraping:YouTubeTranscript:Languages").Get<string[]>() ?? ["tr", "en"];

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            // Windows console codepages (e.g. cp1254) throw UnicodeEncodeError on non-Latin transcript
            // text unless the child process is told to use UTF-8 for its own stdout encoding.
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            foreach (var arg in new[] { "-m", "youtube_transcript_api", videoId, "--languages" }.Concat(languages).Concat(["--format", "text"]))
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null)
            {
                _logger.LogWarning("youtube_transcript_api process could not be started for video {VideoId}", videoId);
                return null;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                _logger.LogDebug("No transcript available for YouTube video {VideoId}: {Stderr}", videoId, stderr.Trim());
                return null;
            }

            // Observed live: on a transient network failure, youtube_transcript_api's CLI has printed a
            // Python exception's str() to stdout while still exiting 0 — the ExitCode check above doesn't
            // catch that. Guard against treating that error text as real transcript content.
            if (LooksLikeErrorOutput(stdout))
            {
                _logger.LogWarning("youtube_transcript_api returned error-like output despite exit code 0 for video {VideoId}: {Output}",
                    videoId, stdout.Length > 200 ? stdout[..200] : stdout);
                return null;
            }

            return stdout.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transcript fetch failed for YouTube video {VideoId}, continuing without it", videoId);
            return null;
        }
    }

    private static bool LooksLikeErrorOutput(string text) =>
        // "Could not retrieve a transcript for the video" is youtube_transcript_api's own umbrella error
        // message — printed (to stdout, with exit code 0) for every failure reason it knows about
        // (disabled captions, blocked IP, video unavailable, etc.), live-confirmed for at least the
        // blocked-IP case. The other checks below cover failure shapes that don't go through that
        // wrapper (e.g. an unhandled network exception's raw traceback).
        text.Contains("Could not retrieve a transcript for the video") ||
        text.Contains("Traceback (most recent call last)") ||
        text.Contains("ConnectionError") ||
        text.Contains("Max retries exceeded") ||
        text.Contains("urllib3.exceptions") ||
        text.Contains("SSLError");
}
