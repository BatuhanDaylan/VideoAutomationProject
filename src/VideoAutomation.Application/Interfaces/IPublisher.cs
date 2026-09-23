using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Application.Interfaces;

public interface IPublisher
{
    Platform Platform { get; }
    Task<Publication> PublishAsync(Video video, CancellationToken ct = default);
    Task<bool> ValidateCredentialsAsync(CancellationToken ct = default);
}
