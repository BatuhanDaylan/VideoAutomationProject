using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.Infrastructure.Publishing;

/// <summary>
/// Long-polls Telegram's getUpdates for the callback_query produced by TelegramNotifier's approve/reject
/// inline keyboard, and drives the actual publish decision. This is the "listener" half of the approval
/// flow — TelegramNotifier only sends the prompt, it can't know when (or whether) the user answers it.
/// A BackgroundService (not a webhook) because this app has no public HTTPS endpoint to receive Telegram
/// webhooks on; polling is the only option that works for a locally-run instance.
/// </summary>
public class TelegramUpdatePollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TelegramUpdatePollingService> _logger;

    private long _offset;

    public TelegramUpdatePollingService(
        IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<TelegramUpdatePollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram onay dinleyicisi başladı.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Poll with the ACTIVE user's bot token (resolved per iteration). No active user / no token
                // yet → wait and re-check; a user may log in and configure Telegram later.
                var token = ResolveActiveSecret(s => s.TelegramBotToken);
                if (string.IsNullOrWhiteSpace(token))
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                    continue;
                }
                await PollOnceAsync(token, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A single bad poll (network blip, malformed payload) must not kill the whole listener —
                // back off briefly and try again rather than exiting the loop.
                _logger.LogWarning(ex, "Telegram getUpdates döngüsünde hata, devam ediliyor");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private string? ResolveActiveSecret(Func<IUserSecrets, string?> pick)
    {
        using var scope = _scopeFactory.CreateScope();
        return pick(scope.ServiceProvider.GetRequiredService<IUserSecrets>());
    }

    private async Task PollOnceAsync(string token, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Telegram");
        // timeout=25 makes this a long-poll: Telegram holds the connection open until an update arrives or
        // 25s pass, instead of us busy-looping every second.
        // Absolute URL — see TelegramNotifier for why a relative "bot{token}/..." string breaks (the
        // token's ':' parses as a URI scheme separator).
        var url = $"https://api.telegram.org/bot{token}/getUpdates?offset={_offset}&timeout=25&allowed_updates=[\"callback_query\"]";

        var response = await client.GetAsync(url, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // No exception is thrown on a non-2xx getUpdates response (a wrong/placeholder token 404s,
            // which is expected until Telegram is configured) — without an explicit delay here the outer
            // loop would immediately retry with zero backoff and hammer the API in a tight loop. The
            // timeout=25 long-poll param only paces the SUCCESS path; this path needs its own pacing.
            _logger.LogWarning("Telegram getUpdates {Status}: {Body}", response.StatusCode, json.Length > 200 ? json[..200] : json);
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
            return;

        foreach (var update in results.EnumerateArray())
        {
            if (update.TryGetProperty("update_id", out var updateIdEl))
                _offset = updateIdEl.GetInt64() + 1;

            if (update.TryGetProperty("callback_query", out var callback))
                await HandleCallbackAsync(token, callback, ct);
        }
    }

    private async Task HandleCallbackAsync(string token, JsonElement callback, CancellationToken ct)
    {
        var callbackId = callback.GetProperty("id").GetString()!;
        var data = callback.TryGetProperty("data", out var d) ? d.GetString() : null;

        await AnswerCallbackQueryAsync(token, callbackId, ct);
        if (string.IsNullOrWhiteSpace(data)) return;

        var action = data.Split(':', 2)[0];
        using var scope = _scopeFactory.CreateScope();

        // Legacy simple approve/reject flow (videos generated via manual "Üret" or the pipeline).
        if (action is "approve" or "reject")
        {
            await HandleLegacyApprovalAsync(scope, data, ct);
            return;
        }

        // Interactive conversation flow (peak-hour candidate → review → tool → generate → publish).
        var chatId = ExtractChatId(callback) ?? scope.ServiceProvider.GetRequiredService<IUserSecrets>().TelegramChatId;
        if (string.IsNullOrWhiteSpace(chatId)) return;
        var convo = scope.ServiceProvider.GetRequiredService<TelegramConversationService>();
        await convo.HandleCallbackAsync(chatId, data, ct);
    }

    private static string? ExtractChatId(JsonElement callback) =>
        callback.TryGetProperty("message", out var m)
        && m.TryGetProperty("chat", out var c)
        && c.TryGetProperty("id", out var id)
            ? id.GetInt64().ToString()
            : null;

    private async Task HandleLegacyApprovalAsync(IServiceScope scope, string data, CancellationToken ct)
    {
        var parts = data.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var videoId))
        {
            _logger.LogWarning("Beklenmeyen Telegram callback_data: {Data}", data);
            return;
        }
        var action = parts[0];

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisherAgent = scope.ServiceProvider.GetRequiredService<IPublisherAgent>();
        var notifier = scope.ServiceProvider.GetRequiredService<TelegramNotifier>();

        var video = await db.Videos.FindAsync([videoId], ct);
        if (video == null)
        {
            _logger.LogWarning("Telegram onayı geldi ama Video {VideoId} bulunamadı", videoId);
            return;
        }
        if (video.Status != VideoStatus.PendingApproval)
        {
            _logger.LogInformation("Video {VideoId} zaten karara bağlanmış (durum={Status}), callback yok sayılıyor", videoId, video.Status);
            return;
        }

        if (action == "approve")
        {
            try
            {
                var publication = await publisherAgent.PublishToPlatformAsync(video, Platform.YouTube, ct);
                await notifier.UpdateApprovalMessageAsync(video, $"✅ Onaylandı ve YouTube'a yüklendi: {publication.PlatformUrl}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video {VideoId} onaylandı ama YouTube'a yükleme başarısız oldu", videoId);
                await notifier.UpdateApprovalMessageAsync(video, $"⚠️ Onaylandı ama yükleme başarısız oldu: {ex.Message}", ct);
            }
        }
        else // reject
        {
            video.Status = VideoStatus.Rejected;
            await db.SaveChangesAsync(ct);
            await notifier.UpdateApprovalMessageAsync(video, "❌ Reddedildi", ct);
        }
    }

    private async Task AnswerCallbackQueryAsync(string token, string callbackId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Telegram");
        var body = JsonSerializer.Serialize(new { callback_query_id = callbackId });
        try
        {
            await client.PostAsync($"https://api.telegram.org/bot{token}/answerCallbackQuery",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "answerCallbackQuery başarısız (callback {CallbackId})", callbackId);
        }
    }
}
