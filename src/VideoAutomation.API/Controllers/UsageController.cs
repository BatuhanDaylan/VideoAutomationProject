using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.API.Controllers;

/// <summary>
/// API usage / cost overview for the current user. Gemini has no usage-read API on the free tier, so its
/// numbers come from our own local counter (ApiUsage). Apify exposes real monthly account usage, fetched
/// live. Pexels is effectively free/unlimited, so only its configured status is reported.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class UsageController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUserSecrets _secrets;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UsageController> _logger;

    public UsageController(AppDbContext db, IUserSecrets secrets, IHttpClientFactory httpClientFactory, ILogger<UsageController> logger)
    {
        _db = db; _secrets = secrets; _httpClientFactory = httpClientFactory; _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // ApiUsages is user-filtered → these are the current user's own counters.
        var rows = await _db.ApiUsages
            .Where(u => u.Provider == "Gemini" && u.Day >= monthStart)
            .ToListAsync(ct);
        var geminiToday = rows.Where(u => u.Day == today).Sum(u => u.Count);
        var geminiMonth = rows.Sum(u => u.Count);

        var apify = await GetApifyUsageAsync(ct);

        return Ok(new
        {
            gemini = new { today = geminiToday, month = geminiMonth, note = "Ücretsiz katmanda kullanım API'si yok — yerel sayaç." },
            apify,
            pexels = new { configured = !string.IsNullOrEmpty(_secrets.PexelsKey), note = "Ücretsiz/limitsiz kullanım." },
        });
    }

    private async Task<object> GetApifyUsageAsync(CancellationToken ct)
    {
        var token = _secrets.ApifyToken;
        if (string.IsNullOrWhiteSpace(token))
            return new { configured = false, monthlyUsageUsd = (double?)null, note = "Apify anahtarı yok." };

        try
        {
            var client = _httpClientFactory.CreateClient("Apify");
            var resp = await client.GetAsync($"v2/users/me/usage/monthly?token={token}", ct);
            if (!resp.IsSuccessStatusCode)
                return new { configured = true, monthlyUsageUsd = (double?)null, note = $"Apify kullanımı okunamadı ({(int)resp.StatusCode})." };

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var usd = ExtractApifyUsd(doc.RootElement);
            return new { configured = true, monthlyUsageUsd = usd, note = usd == null ? "Kullanım tutarı ayrıştırılamadı." : "Bu ayki tahmini kullanım (USD)." };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Apify kullanımı çekilemedi");
            return new { configured = true, monthlyUsageUsd = (double?)null, note = "Apify'a ulaşılamadı." };
        }
    }

    // Apify's monthly-usage payload nests the USD figure under data.* — field names have shifted across API
    // versions, so probe the known candidates defensively rather than binding a fixed shape.
    private static double? ExtractApifyUsd(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in new[] { "usdBeforeVolumeDiscount", "totalUsageCreditsUsdBeforeVolumeDiscount", "totalUsageUsd", "usdSpent" })
        {
            if (data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
                return Math.Round(d, 2);
        }
        return null;
    }
}
