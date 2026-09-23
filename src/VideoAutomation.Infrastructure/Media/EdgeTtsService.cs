using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Media;

/// <summary>
/// Shells out to the Python `edge-tts` CLI (no official .NET SDK exists for Microsoft Edge/Azure Neural
/// voices — Template.TTSVoice defaults like "tr-TR-AhmetNeural" are edge-tts voice names). Narration text
/// is written to a temp file and passed via --file rather than inline --text, to avoid shell-escaping
/// issues with Turkish quotes/apostrophes when Process starts the executable directly (no shell involved,
/// but --file sidesteps command-line length/encoding edge cases entirely).
/// </summary>
public class EdgeTtsService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EdgeTtsService> _logger;

    public EdgeTtsService(IConfiguration configuration, ILogger<EdgeTtsService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string text, string voice, string outputPath, CancellationToken ct)
    {
        var pythonExe = _configuration.GetValue("Media:EdgeTts:PythonExecutable", "python")!;
        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        var textFile = Path.Combine(Path.GetTempPath(), $"edge-tts-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(textFile, text, ct);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-m", "edge_tts", "--voice", voice, "--file", textFile, "--write-media", outputPath })
                psi.ArgumentList.Add(arg);

            using var process = StartProcess(psi, pythonExe);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                _logger.LogError("edge-tts failed (exit={ExitCode}): {Stderr}", process.ExitCode, stderr);
                throw new VideoGenerationException(
                    $"edge-tts başarısız oldu (exit={process.ExitCode}): {stderr}. " +
                    $"'{pythonExe} -m pip install edge-tts' ile kurulu olduğundan emin olun.");
            }

            return outputPath;
        }
        finally
        {
            if (File.Exists(textFile)) File.Delete(textFile);
        }
    }

    private static Process StartProcess(ProcessStartInfo psi, string pythonExe)
    {
        try
        {
            return Process.Start(psi) ?? throw new VideoGenerationException("edge-tts process başlatılamadı.");
        }
        catch (Win32Exception ex)
        {
            throw new VideoGenerationException(
                $"'{pythonExe}' çalıştırılabilir dosyası bulunamadı. Python kurulu mu, PATH'te mi? ({ex.Message})", ex);
        }
    }
}
