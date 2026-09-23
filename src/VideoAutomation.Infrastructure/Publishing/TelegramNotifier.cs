using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.Publishing;

/// <summary>
/// INotifier via the Telegram Bot API. Sends the composed video file itself (not just a link) with an
/// inline "✅ Onayla" / "❌ Reddet" keyboard; TelegramUpdatePollingService is the other half — it long-polls
/// for the callback_query this keyboard produces and drives the actual publish decision.
/// </summary>
public class TelegramNotifier : INotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserSecrets _secrets;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(IHttpClientFactory httpClientFactory, IConfiguration configuration, IUserSecrets secrets, ILogger<TelegramNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _secrets = secrets;
        _logger = logger;
    }

    public async Task RequestApprovalAsync(Video video, CancellationToken ct = default)
    {
        var (token, chatId) = GetCredentials();

        if (string.IsNullOrWhiteSpace(video.OutputFilePath) || !File.Exists(video.OutputFilePath))
            throw new PublishingException("Telegram", $"Video {video.Id} için çıktı dosyası yok, Telegram'a gönderilemedi.");

        var caption = BuildCaption(video);
        var replyMarkup = JsonSerializer.Serialize(new
        {
            inline_keyboard = new[]
            {
                new[]
                {
                    new { text = "✅ Onayla ve YouTube'a yükle", callback_data = $"approve:{video.Id}" },
                    new { text = "❌ Reddet", callback_data = $"reject:{video.Id}" },
                },
            },
        });

        using var form = new MultipartFormDataContent
        {
            { new StringContent(chatId), "chat_id" },
            { new StringContent(caption), "caption" },
            { new StringContent(replyMarkup), "reply_markup" },
        };

        var videoBytes = await File.ReadAllBytesAsync(video.OutputFilePath, ct);
        var videoContent = new ByteArrayContent(videoBytes);
        videoContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
        form.Add(videoContent, "video", Path.GetFileName(video.OutputFilePath));

        var client = _httpClientFactory.CreateClient("Telegram");
        HttpResponseMessage response;
        try
        {
            // Absolute URL, not a relative path resolved against BaseAddress: the bot token contains a
            // ':' (e.g. "123:AAE..."), and a purely relative "bot123:AAE.../sendVideo" string parses as an
            // absolute URI with scheme "bot123" (valid URI scheme grammar) — live-confirmed
            // NotSupportedException "The 'bot8901429932' scheme is not supported." A fully-qualified
            // "https://..." string has no such ambiguity.
            response = await client.PostAsync($"https://api.telegram.org/bot{token}/sendVideo", form, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PublishingException("Telegram", $"Telegram'a video gönderimi başarısız: {ex.Message}", ex);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new PublishingException("Telegram", $"Telegram API {(int)response.StatusCode} döndürdü: {json[..Math.Min(json.Length, 300)]}");

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("message_id", out var msgIdEl))
            throw new PublishingException("Telegram", $"Telegram yanıtında message_id yok: {json[..Math.Min(json.Length, 300)]}");

        video.TelegramMessageId = msgIdEl.GetInt64();
        _logger.LogInformation("Video {VideoId} onay için Telegram'a gönderildi (message {MessageId})", video.Id, video.TelegramMessageId);
    }

    /// <summary>Edits the original approval message to remove the buttons and show the outcome.</summary>
    public async Task UpdateApprovalMessageAsync(Video video, string resultText, CancellationToken ct = default)
    {
        if (video.TelegramMessageId is not { } messageId) return;

        var (token, chatId) = GetCredentials();
        var client = _httpClientFactory.CreateClient("Telegram");

        var body = JsonSerializer.Serialize(new
        {
            chat_id = chatId,
            message_id = messageId,
            caption = $"{BuildCaption(video)}\n\n{resultText}",
        });

        try
        {
            var response = await client.PostAsync($"https://api.telegram.org/bot{token}/editMessageCaption",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Telegram mesajı güncellenemedi (video {VideoId}): {Status}", video.Id, response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Telegram mesajı güncellenirken hata (video {VideoId})", video.Id);
        }
    }

    private (string Token, string ChatId) GetCredentials()
    {
        var token = _secrets.TelegramBotToken;
        var chatId = _secrets.TelegramChatId;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
            throw new PublishingException("Telegram", "Telegram bot token'ı veya chat id'si yapılandırılmamış — panelden (🔑 API Anahtarları) gir.");
        return (token, chatId);
    }

    private static string BuildCaption(Video video)
    {
        var caption = $"🎬 {video.Title}\n\n⏱ {video.DurationSeconds}sn | {video.VisualMode}";
        return caption.Length <= 1024 ? caption : caption[..1024];
    }
}
