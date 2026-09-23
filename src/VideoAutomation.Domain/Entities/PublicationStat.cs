namespace VideoAutomation.Domain.Entities;

/// <summary>
/// A point-in-time snapshot of a published video's public stats (views/likes/comments), captured by the
/// daily stats-refresh job. Kept as a time series (one row per capture) so growth over time — and, later,
/// which trends/scripts actually performed — can be analysed rather than only the latest number.
/// CreatedAt (BaseEntity) is the capture time.
/// </summary>
public class PublicationStat : BaseEntity
{
    public int UserId { get; set; }

    public int PublicationId { get; set; }
    public Publication Publication { get; set; } = null!;

    public long ViewCount { get; set; }
    public long LikeCount { get; set; }
    public long CommentCount { get; set; }
}
