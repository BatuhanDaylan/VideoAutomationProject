using Microsoft.EntityFrameworkCore;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.API.Security;

/// <summary>
/// Resolves and decrypts the current user's API keys. Scoped — one per request/job scope. The user id comes
/// from ICurrentUserId (JWT for HTTP, active user for background jobs). Keys are loaded lazily from the DB
/// (once per scope) and decrypted on access; a null/empty key means "not set".
/// </summary>
public class UserSecrets : IUserSecrets
{
    private readonly ICurrentUserId _currentUser;
    private readonly AppDbContext _db;
    private readonly ICryptoService _crypto;

    private bool _loaded;
    private User? _user;

    public UserSecrets(ICurrentUserId currentUser, AppDbContext db, ICryptoService crypto)
    {
        _currentUser = currentUser; _db = db; _crypto = crypto;
    }

    public int? UserId => _currentUser.Value;

    private User? Load()
    {
        if (_loaded) return _user;
        _loaded = true;
        // Users has no query filter, but be explicit anyway so a global filter never hides the row.
        _user = _currentUser.Value is { } id ? _db.Users.IgnoreQueryFilters().FirstOrDefault(u => u.Id == id) : null;
        return _user;
    }

    private string? Dec(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) return null;
        try { var s = _crypto.Decrypt(enc); return string.IsNullOrWhiteSpace(s) ? null : s; }
        catch { return null; }
    }

    public IReadOnlyList<string> GeminiApiKeys
    {
        get
        {
            var blob = Dec(Load()?.GeminiApiKeysEnc);
            return string.IsNullOrWhiteSpace(blob)
                ? Array.Empty<string>()
                : blob.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    public string? YouTubeApiKey => Dec(Load()?.YouTubeApiKeyEnc);
    public string? YouTubeClientId => Dec(Load()?.YouTubeClientIdEnc);
    public string? YouTubeClientSecret => Dec(Load()?.YouTubeClientSecretEnc);
    public string? ApifyToken => Dec(Load()?.ApifyApiTokenEnc);
    public string? PexelsKey => Dec(Load()?.PexelsApiKeyEnc);
    public string? AiVideoToken => Dec(Load()?.AiVideoApiTokenEnc);
    public string? TelegramBotToken => Dec(Load()?.TelegramBotTokenEnc);
    public string? TelegramChatId => Dec(Load()?.TelegramChatIdEnc);
}
