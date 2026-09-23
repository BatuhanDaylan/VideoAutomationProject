using VideoAutomation.Domain.Entities;

namespace VideoAutomation.Application.Interfaces;

public interface IVideoComposer
{
    /// <summary>
    /// Composes the final video from Video.Scenes (which must already have AudioFilePath and
    /// VideoFilePathA/B populated). Template is required alongside Video because subtitle styling,
    /// A/B split, avatar injection, and BGM are all Template-level settings that Video itself doesn't
    /// carry (Video only has a TemplateType enum, not a Template FK).
    /// </summary>
    Task<string> ComposeVideoAsync(Video video, Template template, CancellationToken ct = default);
    Task<string> GenerateTTSAsync(string text, string voice, string outputPath, CancellationToken ct = default);
    Task RemoveSilenceAsync(string audioPath, string outputPath, CancellationToken ct = default);
}
