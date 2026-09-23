using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Infrastructure.Media;

/// <summary>
/// IStockVideoProvider backed by AiVideoClient (aimlapi.com / Kling). Replaces the earlier
/// HuggingFace-image-plus-Ken-Burns approach with a real text-to-video model — no still-image
/// intermediate, no zoompan synthesis, the clip itself is AI-generated.
/// </summary>
public class AiVideoStockProvider : IStockVideoProvider
{
    public VisualMode SupportedMode => VisualMode.AIGenerated;

    private readonly AiVideoClient _client;

    public AiVideoStockProvider(AiVideoClient client)
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
        var path = Path.Combine(outputDir, $"aivideo-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }
}
