namespace VideoAutomation.Domain.Entities;

/// <summary>
/// An application user. The app runs locally and one user is "active" (logged in) at a time, but multiple
/// accounts can exist and each keeps its own data (UserId on the other entities) and its own API keys.
/// Passwords are stored as a PBKDF2 hash + per-user salt (never reversible). API keys are stored AES-encrypted
/// (reversible — the app must decrypt them to call the providers) using the app's ENCRYPTION_MASTER_KEY.
/// </summary>
public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    // PBKDF2(password, salt) — Base64. Salt is per-user, also Base64.
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;

    // ── Per-user API keys, AES-encrypted at rest (null/empty = not set) ──────────────────────────
    // Gemini keys are stored as a single encrypted, comma-separated blob (the pool supports multiple).
    public string? GeminiApiKeysEnc { get; set; }
    public string? YouTubeApiKeyEnc { get; set; }        // YouTube Data API (trend search)
    public string? YouTubeClientIdEnc { get; set; }      // OAuth desktop client (upload)
    public string? YouTubeClientSecretEnc { get; set; }
    public string? ApifyApiTokenEnc { get; set; }
    public string? PexelsApiKeyEnc { get; set; }
    public string? AiVideoApiTokenEnc { get; set; }
    public string? TelegramBotTokenEnc { get; set; }
    public string? TelegramChatIdEnc { get; set; }
}
