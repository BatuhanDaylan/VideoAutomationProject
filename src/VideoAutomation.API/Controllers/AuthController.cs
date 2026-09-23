using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAutomation.API.Security;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Infrastructure.Security;

namespace VideoAutomation.API.Controllers;

/// <summary>
/// Registration / login. Passwords are PBKDF2-hashed; on success we return a JWT the SPA stores and sends as
/// a Bearer header. Every other controller is [Authorize]d, so the token is required for all app operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher _hasher;
    private readonly JwtTokenService _jwt;
    private readonly ICryptoService _crypto;
    private readonly IActiveUserProvider _activeUser;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext db, PasswordHasher hasher, JwtTokenService jwt, ICryptoService crypto, IActiveUserProvider activeUser, ILogger<AuthController> logger)
    {
        _db = db; _hasher = hasher; _jwt = jwt; _crypto = crypto; _activeUser = activeUser; _logger = logger;
    }

    public class RegisterRequest
    {
        public string Email { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class LoginRequest
    {
        public string EmailOrUsername { get; set; } = "";
        public string Password { get; set; } = "";
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        var username = req.Username?.Trim() ?? "";
        if (email.Length < 3 || !email.Contains('@')) return BadRequest(new { error = "Geçerli bir e-posta gir." });
        if (username.Length < 2) return BadRequest(new { error = "Kullanıcı adı en az 2 karakter olmalı." });
        if ((req.Password?.Length ?? 0) < 6) return BadRequest(new { error = "Parola en az 6 karakter olmalı." });

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "Bu e-posta zaten kayıtlı." });
        if (await _db.Users.AnyAsync(u => u.Username == username))
            return Conflict(new { error = "Bu kullanıcı adı alınmış." });

        var (hash, salt) = _hasher.Hash(req.Password!);
        var user = new User { Email = email, Username = username, PasswordHash = hash, PasswordSalt = salt };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Yeni kullanıcı kaydı: {Username} ({Email})", username, email);

        _activeUser.ActiveUserId = user.Id;  // this user is now the active one (background jobs run as them)
        return Ok(new { token = _jwt.CreateToken(user), user = Public(user) });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var key = req.EmailOrUsername?.Trim() ?? "";
        var keyLower = key.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == keyLower || u.Username == key);
        if (user == null || !_hasher.Verify(req.Password ?? "", user.PasswordHash, user.PasswordSalt))
            return Unauthorized(new { error = "E-posta/kullanıcı adı veya parola hatalı." });

        _activeUser.ActiveUserId = user.Id;  // this user is now the active one (background jobs run as them)
        return Ok(new { token = _jwt.CreateToken(user), user = Public(user) });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.GetUserId() is not { } id) return Unauthorized();
        var user = await _db.Users.FindAsync(id);
        if (user == null) return Unauthorized();
        return Ok(new { user = Public(user) });
    }

    // ── Per-user API keys (encrypted at rest) ────────────────────────────────────────────────────

    /// <summary>Returns which keys the current user has configured — masked (last 4 chars), never the full
    /// value — so the UI can show status without ever shipping a secret back to the browser.</summary>
    [Authorize]
    [HttpGet("keys")]
    public async Task<IActionResult> GetKeys()
    {
        if (User.GetUserId() is not { } id) return Unauthorized();
        var u = await _db.Users.FindAsync(id);
        if (u == null) return Unauthorized();

        return Ok(new
        {
            gemini = GeminiStatus(u.GeminiApiKeysEnc),
            youtubeApiKey = Status(u.YouTubeApiKeyEnc),
            youtubeClientId = Status(u.YouTubeClientIdEnc),
            youtubeClientSecret = Status(u.YouTubeClientSecretEnc),
            apify = Status(u.ApifyApiTokenEnc),
            pexels = Status(u.PexelsApiKeyEnc),
            aiVideo = Status(u.AiVideoApiTokenEnc),
            telegramBotToken = Status(u.TelegramBotTokenEnc),
            telegramChatId = Status(u.TelegramChatIdEnc),
        });
    }

    public class SaveKeysRequest
    {
        // Each field: null = leave unchanged; non-null = set (encrypt & store); whitespace = clear.
        public string? Gemini { get; set; }
        public string? YoutubeApiKey { get; set; }
        public string? YoutubeClientId { get; set; }
        public string? YoutubeClientSecret { get; set; }
        public string? Apify { get; set; }
        public string? Pexels { get; set; }
        public string? AiVideo { get; set; }
        public string? TelegramBotToken { get; set; }
        public string? TelegramChatId { get; set; }
    }

    [Authorize]
    [HttpPut("keys")]
    public async Task<IActionResult> SaveKeys([FromBody] SaveKeysRequest req)
    {
        if (User.GetUserId() is not { } id) return Unauthorized();
        var u = await _db.Users.FindAsync(id);
        if (u == null) return Unauthorized();

        u.GeminiApiKeysEnc = Apply(u.GeminiApiKeysEnc, req.Gemini);
        u.YouTubeApiKeyEnc = Apply(u.YouTubeApiKeyEnc, req.YoutubeApiKey);
        u.YouTubeClientIdEnc = Apply(u.YouTubeClientIdEnc, req.YoutubeClientId);
        u.YouTubeClientSecretEnc = Apply(u.YouTubeClientSecretEnc, req.YoutubeClientSecret);
        u.ApifyApiTokenEnc = Apply(u.ApifyApiTokenEnc, req.Apify);
        u.PexelsApiKeyEnc = Apply(u.PexelsApiKeyEnc, req.Pexels);
        u.AiVideoApiTokenEnc = Apply(u.AiVideoApiTokenEnc, req.AiVideo);
        u.TelegramBotTokenEnc = Apply(u.TelegramBotTokenEnc, req.TelegramBotToken);
        u.TelegramChatIdEnc = Apply(u.TelegramChatIdEnc, req.TelegramChatId);

        await _db.SaveChangesAsync();
        _logger.LogInformation("Kullanıcı {Id} API anahtarlarını güncelledi.", id);
        return await GetKeys();
    }

    // null incoming → keep existing; whitespace → clear; otherwise encrypt the new value.
    private string? Apply(string? existingEnc, string? incoming)
    {
        if (incoming == null) return existingEnc;
        return string.IsNullOrWhiteSpace(incoming) ? null : _crypto.Encrypt(incoming.Trim());
    }

    // Gemini can hold several keys (comma-separated) — report how many, plus a masked hint of the last one.
    private object GeminiStatus(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) return new { set = false, hint = (string?)null, count = 0 };
        try
        {
            var keys = _crypto.Decrypt(enc).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var last = keys.LastOrDefault() ?? "";
            var hint = last.Length <= 4 ? new string('•', last.Length) : "••••" + last[^4..];
            return new { set = keys.Length > 0, hint, count = keys.Length };
        }
        catch { return new { set = true, hint = (string?)null, count = 1 }; }
    }

    private object Status(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) return new { set = false, hint = (string?)null };
        string? hint = null;
        try
        {
            var plain = _crypto.Decrypt(enc);
            hint = plain.Length <= 4 ? new string('•', plain.Length) : "••••" + plain[^4..];
        }
        catch { /* corrupt/undecryptable → still report as set */ }
        return new { set = true, hint };
    }

    private static object Public(User u) => new { u.Id, u.Email, u.Username };
}
