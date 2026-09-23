using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Application.Interfaces.Agents;

/// <summary>
/// Publisher Agent: Publishes videos to YouTube, Instagram, and TikTok.
/// </summary>
public interface IPublisherAgent
{
    Task<List<Publication>> PublishToAllPlatformsAsync(Video video, CancellationToken ct = default);
    Task<Publication> PublishToPlatformAsync(Video video, Platform platform, CancellationToken ct = default);
}
