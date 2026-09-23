namespace VideoAutomation.Domain.Entities;

/// <summary>
/// A per-user, per-provider, per-day call counter. Exists because some providers (Gemini free tier) expose
/// NO usage-read API, so the only way to show "how much have I used today/this month" is to count locally.
/// One row per (UserId, Provider, Day); incremented atomically via SQL UPSERT so concurrent calls don't race.
/// </summary>
public class ApiUsage : BaseEntity
{
    public int UserId { get; set; }
    public string Provider { get; set; } = string.Empty; // e.g. "Gemini"
    public DateTime Day { get; set; }                     // date at 00:00 UTC
    public long Count { get; set; }
}
