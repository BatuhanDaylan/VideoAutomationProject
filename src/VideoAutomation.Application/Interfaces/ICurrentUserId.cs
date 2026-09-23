namespace VideoAutomation.Application.Interfaces;

/// <summary>
/// Just the current user's id — no DB access (unlike IUserSecrets, which loads + decrypts keys). Kept
/// dependency-free so AppDbContext can use it for global query filters + stamping UserId on inserts without
/// creating a cycle (IUserSecrets depends on AppDbContext). Resolved from the JWT on an HTTP request, else
/// the active user for a background job.
/// </summary>
public interface ICurrentUserId
{
    int? Value { get; }

    /// <summary>Pin the current user for this scope, overriding the JWT/active-user resolution. Used by
    /// background jobs (Telegram flow, etc.) to run as the OWNER of the work (e.g. the TelegramSession's
    /// UserId) rather than whoever happens to be the globally-active user — so keys + data scope are correct
    /// even if the active user changed since the work was queued.</summary>
    void Override(int userId);
}
