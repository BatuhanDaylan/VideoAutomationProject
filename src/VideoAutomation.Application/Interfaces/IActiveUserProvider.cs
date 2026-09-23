namespace VideoAutomation.Application.Interfaces;

/// <summary>
/// Tracks the currently-active user for contexts that have no HTTP request (Hangfire jobs, the Telegram
/// long-poll BackgroundService). The app runs locally with one active user at a time — "o an giriş yapmış
/// kullanıcı" — so background work operates as this user. Set on login/register; initialized at startup to
/// the most recent user so scheduled jobs keep working after a restart. Singleton.
/// </summary>
public interface IActiveUserProvider
{
    int? ActiveUserId { get; set; }
}
