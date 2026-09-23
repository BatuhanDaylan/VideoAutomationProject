using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Agents;

/// <summary>
/// Publisher Agent: uploads an approved Video to a platform via the matching IPublisher and persists the
/// resulting Publication. Lives in Infrastructure (not Application/Services/Agents) for the same reason as
/// every other agent here — needs direct AppDbContext access.
/// </summary>
public class PublisherAgent : IPublisherAgent
{
    private readonly IEnumerable<IPublisher> _publishers;
    private readonly AppDbContext _db;
    private readonly ILogger<PublisherAgent> _logger;

    public PublisherAgent(IEnumerable<IPublisher> publishers, AppDbContext db, ILogger<PublisherAgent> logger)
    {
        _publishers = publishers;
        _db = db;
        _logger = logger;
    }

    public async Task<Publication> PublishToPlatformAsync(Video video, Platform platform, CancellationToken ct = default)
    {
        var publisher = _publishers.FirstOrDefault(p => p.Platform == platform)
            ?? throw new PublishingException(platform.ToString(), $"'{platform}' için kayıtlı bir IPublisher yok.");

        video.Status = VideoStatus.Publishing;
        await _db.SaveChangesAsync(ct);

        Publication publication;
        try
        {
            publication = await publisher.PublishAsync(video, ct);
        }
        catch (Exception ex)
        {
            video.Status = VideoStatus.Failed;
            var failed = new Publication
            {
                VideoId = video.Id,
                Platform = platform,
                IsPublished = false,
                ErrorMessage = ex.Message,
            };
            _db.Publications.Add(failed);
            // CancellationToken.None, deliberately not `ct`: if the failure was ITSELF caused by `ct`
            // being cancelled (e.g. the caller's HTTP request got aborted mid-upload — live-reproduced by
            // curl --max-time expiring during YouTube's OAuth consent wait), saving with the same token
            // would throw again immediately and the video would be stuck at Status=Publishing forever,
            // silently, with no Failed record. This save must complete regardless of why we got here.
            await _db.SaveChangesAsync(CancellationToken.None);

            if (ex is PublishingException) throw;
            throw new PublishingException(platform.ToString(), ex.Message, ex);
        }

        _db.Publications.Add(publication);
        video.Status = VideoStatus.Published;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Video {VideoId} {Platform}'a yayınlandı: {Url}", video.Id, platform, publication.PlatformUrl);
        return publication;
    }

    public async Task<List<Publication>> PublishToAllPlatformsAsync(Video video, CancellationToken ct = default)
    {
        var results = new List<Publication>();
        foreach (var publisher in _publishers)
        {
            try
            {
                results.Add(await PublishToPlatformAsync(video, publisher.Platform, ct));
            }
            catch (PublishingException ex)
            {
                _logger.LogWarning(ex, "Video {VideoId} {Platform}'a yayınlanamadı, diğer platformlarla devam ediliyor", video.Id, publisher.Platform);
            }
        }

        return results;
    }
}
