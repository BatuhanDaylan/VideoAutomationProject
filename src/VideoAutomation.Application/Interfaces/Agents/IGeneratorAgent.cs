using VideoAutomation.Domain.Entities;

namespace VideoAutomation.Application.Interfaces.Agents;

/// <summary>
/// Generator Agent: Produces video from script, assets, TTS, and FFmpeg composition.
/// </summary>
public interface IGeneratorAgent
{
    /// <param name="requestApproval">
    /// When true (default), after composing the video the agent sends the simple approve/reject Telegram
    /// notification. The interactive Telegram conversation flow passes false and sends its own review message.
    /// </param>
    Task<Video> GenerateVideoAsync(Script script, Template template, CancellationToken ct = default, bool requestApproval = true);
}
