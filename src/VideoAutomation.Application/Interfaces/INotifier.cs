using VideoAutomation.Domain.Entities;

namespace VideoAutomation.Application.Interfaces;

/// <summary>
/// Sends a composed video out for the user's approve/reject decision before it's published anywhere.
/// Separate from IPublisher: a notifier tells a human, a publisher uploads to a platform — Telegram is
/// the former even though it also happens to be a messaging platform.
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Sends the video file with an approve/reject prompt. Implementations should stamp an identifier for
    /// the sent message onto <paramref name="video"/> (e.g. Video.TelegramMessageId) so a later step can
    /// edit it in place once the decision arrives — callers must save the video after this call.
    /// </summary>
    Task RequestApprovalAsync(Video video, CancellationToken ct = default);
}
