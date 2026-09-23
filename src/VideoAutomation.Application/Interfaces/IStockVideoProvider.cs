using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Application.Interfaces;

public interface IStockVideoProvider
{
    /// <summary>Which VisualMode this provider serves — GeneratorAgent picks among registered providers by this.</summary>
    VisualMode SupportedMode { get; }

    Task<(string videoPathA, string videoPathB)> GetDualVideosAsync(string keyword, string outputDir, CancellationToken ct = default);
    Task<string> GetSingleVideoAsync(string keyword, string outputDir, CancellationToken ct = default);
}
