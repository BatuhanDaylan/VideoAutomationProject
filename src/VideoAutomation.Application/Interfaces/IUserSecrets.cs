namespace VideoAutomation.Application.Interfaces;

/// <summary>
/// The decrypted API keys of the CURRENT user — the logged-in user for an HTTP request, or the active user
/// for a background job. Every service that used to read <c>IConfiguration["ApiKeys:*"]</c> now reads here
/// instead, so each user runs the pipeline with their own keys. Scoped: resolved once per request/job scope.
/// </summary>
public interface IUserSecrets
{
    /// <summary>The resolved user id, or null if there's no logged-in/active user.</summary>
    int? UserId { get; }

    IReadOnlyList<string> GeminiApiKeys { get; }   // one or more (comma-separated blob), for the key pool
    string? YouTubeApiKey { get; }                 // YouTube Data API (trend search)
    string? YouTubeClientId { get; }               // OAuth desktop client (upload)
    string? YouTubeClientSecret { get; }
    string? ApifyToken { get; }
    string? PexelsKey { get; }
    string? AiVideoToken { get; }
    string? TelegramBotToken { get; }
    string? TelegramChatId { get; }
}
