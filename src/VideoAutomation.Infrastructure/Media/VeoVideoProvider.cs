using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Infrastructure.Media;

/// <summary>
/// IStockVideoProvider backed by VeoClient (Google Veo via the Gemini API). Registered for
/// VisualMode.AIGenerated in place of AiVideoStockProvider (aimlapi/Kling) while AI_VIDEO_API_TOKEN
/// is unset — swapping back (or to any future provider) is a one-line DI change in Program.cs,
/// GeneratorAgent is untouched either way.
/// </summary>
public class VeoVideoProvider : IStockVideoProvider
{
    public VisualMode SupportedMode => VisualMode.AIGenerated;

    private readonly VeoClient _client;

    public VeoVideoProvider(VeoClient client)
    {
        _client = client;
    }

    public async Task<(string videoPathA, string videoPathB)> GetDualVideosAsync(string keyword, string outputDir, CancellationToken ct = default)
    {
        var pathA = await GenerateClipAsync(keyword, outputDir, ct);
        var pathB = await GenerateClipAsync($"{keyword}, alternate angle", outputDir, ct);
        return (pathA, pathB);
    }

    public Task<string> GetSingleVideoAsync(string keyword, string outputDir, CancellationToken ct = default)
        => GenerateClipAsync(keyword, outputDir, ct);

    private async Task<string> GenerateClipAsync(string prompt, string outputDir, CancellationToken ct)
    {
        var bytes = await _client.GenerateVideoAsync(prompt, ct);

        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, $"veo-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }
}
