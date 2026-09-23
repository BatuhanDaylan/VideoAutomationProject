using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.Persistence;

namespace VideoAutomation.Infrastructure.Publishing;

/// <summary>
/// Drives the interactive, autonomous Telegram video-creation flow. At peak hours a Hangfire job sends the
/// top-N viral YouTube candidates; the user then walks through pick → review content → choose tool → review
/// video → publish, with change/regenerate loops. Heavy steps (analyze/generate/publish) run as Hangfire
/// jobs so the poll loop stays responsive, and each job sends the next Telegram message when it finishes.
/// State lives in a persisted TelegramSession so the flow survives a restart.
/// </summary>
public class TelegramConversationService
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly IScoutAgent _scout;
    private readonly IAnalyzerAgent _analyzer;
    private readonly IGeneratorAgent _generator;
    private readonly IPublisherAgent _publisher;
    private readonly IBackgroundJobClient _jobs;
    private readonly IUserSecrets _secrets;
    private readonly ICurrentUserId _currentUser;
    private readonly ILogger<TelegramConversationService> _logger;

    public TelegramConversationService(
        IHttpClientFactory http, IConfiguration config, AppDbContext db,
        IScoutAgent scout, IAnalyzerAgent analyzer, IGeneratorAgent generator, IPublisherAgent publisher,
        IBackgroundJobClient jobs, IUserSecrets secrets, ICurrentUserId currentUser, ILogger<TelegramConversationService> logger)
    {
        _http = http; _config = config; _db = db;
        _scout = scout; _analyzer = analyzer; _generator = generator; _publisher = publisher;
        _jobs = jobs; _secrets = secrets; _currentUser = currentUser; _logger = logger;
    }

    // ── Autonomous entry point (Hangfire recurring at peak hours) ──────────────────────────────
    public async Task RunAutonomousBatchAsync(int withinHours, CancellationToken ct = default)
    {
        var chatId = _secrets.TelegramChatId;
        if (string.IsNullOrWhiteSpace(_secrets.TelegramBotToken) || string.IsNullOrWhiteSpace(chatId))
        {
            _logger.LogInformation("Otonom Telegram partisi atlandı — Telegram yapılandırılmamış.");
            return;
        }

        // Idempotency guard: don't send another candidate batch if one is already waiting on the user. This
        // stops duplicates when several enqueued jobs replay together (e.g. jobs left over from a previous
        // run all fire when the Hangfire server restarts) or a peak-hour job overlaps a manual batch-now.
        // Peak hours are hours apart, so a 20-minute window never suppresses a legitimate scheduled batch.
        var recentCutoff = DateTime.UtcNow.AddMinutes(-20);
        var alreadyPending = await _db.TelegramSessions.AnyAsync(s =>
            s.ChatId == chatId && s.IsActive
            && s.Stage == TelegramFlowStage.AwaitingSelection
            && s.CreatedAt >= recentCutoff, ct);
        if (alreadyPending)
        {
            _logger.LogInformation("Otonom parti atlandı — son 20 dk içinde kullanıcının cevap beklediği bir aday partisi zaten var.");
            return;
        }

        // Always a FRESH YouTube scan first (free Data API) so we never send stale trends — then pick from
        // videos published within this trigger's window. Scan failure is non-fatal (fall back to existing DB).
        try { await _scout.DiscoverTrendsAsync("TR", new[] { Platform.YouTube }, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Otonom parti: tarama başarısız, mevcut trendlerle devam"); }

        var count = _config.GetValue("Telegram:CandidatesPerBatch", 5);
        var candidates = await RecentViralCandidatesAsync(new HashSet<int>(), count, withinHours, ct);

        if (candidates.Count == 0)
        {
            _logger.LogInformation("Otonom parti: gönderilecek aday yok (son {H} saat).", withinHours);
            return;
        }

        _logger.LogInformation("Otonom parti: son {H} saatte yayınlanan {N} aday seçildi.", withinHours, candidates.Count);
        await StartCandidateFlowAsync(chatId!, candidates, withinHours, ct);
    }

    public async Task StartCandidateFlowAsync(string chatId, List<Trend> candidates, int withinHours, CancellationToken ct = default)
    {
        // One active flow per chat: retire any previous one.
        var old = await _db.TelegramSessions.Where(s => s.ChatId == chatId && s.IsActive).ToListAsync(ct);
        foreach (var s in old) s.IsActive = false;

        var session = new TelegramSession
        {
            ChatId = chatId,
            Stage = TelegramFlowStage.AwaitingSelection,
            CandidateTrendIds = JsonSerializer.Serialize(candidates.Select(c => c.Id)),
            IsActive = true,
            WithinHours = withinHours,
        };
        _db.TelegramSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        await SendCandidatesAsync(chatId, candidates, isFirst: true, ct);
        _logger.LogInformation("Telegram akışı başlatıldı: {Count} aday (session {Id})", candidates.Count, session.Id);
    }

    /// <summary>Sends one message per candidate (link preview + "Seç" button), then a "farklı videolar öner"
    /// message so the user can request a fresh batch if none of these appealed.</summary>
    private async Task SendCandidatesAsync(string chatId, List<Trend> candidates, bool isFirst, CancellationToken ct)
    {
        await SendMessageAsync(chatId,
            isFirst ? $"🔥 {candidates.Count} viral aday — beğendiğin videoyu seç:" : $"🔁 İşte farklı {candidates.Count} video:",
            ct: ct);
        for (var i = 0; i < candidates.Count; i++)
        {
            var t = candidates[i];
            var caption = $"{i + 1}️⃣ {Trunc(t.Title, 140)}\n👁 {t.ViewCount:N0}  ·  📈 viral {t.FinalViralityScore:F0}\n{t.SourceUrl}";
            var kb = InlineKeyboard(new[] { Btn($"▶️ {i + 1}. videoyu seç", $"pick:{t.Id}") });
            await SendMessageAsync(chatId, caption, kb, ct);
        }
        var refreshKb = InlineKeyboard(new[] { Btn("🔄 Hiçbiri olmadı — farklı videolar öner", "refresh:0") });
        await SendMessageAsync(chatId, "Beğenmedin mi? Farklı öneriler getireyim 👇", refreshKb, ct);
    }

    /// <summary>Handles the "refresh" button: fetches the next N unprocessed YouTube trends by virality,
    /// excluding those already shown in this session. Re-scans once if the pool is exhausted; appends the
    /// new ids to the session so a further refresh keeps advancing rather than repeating.</summary>
    private async Task SendDifferentCandidatesAsync(TelegramSession session, CancellationToken ct)
    {
        var shown = new HashSet<int>();
        try { shown = JsonSerializer.Deserialize<HashSet<int>>(session.CandidateTrendIds) ?? new(); }
        catch { /* corrupt/empty json → treat as nothing shown */ }

        var count = _config.GetValue("Telegram:CandidatesPerBatch", 5);
        var more = await RecentViralCandidatesAsync(shown, count, session.WithinHours, ct);

        // Pool exhausted → one fresh scan, then retry (same window the batch used).
        if (more.Count == 0)
        {
            await SendMessageAsync(session.ChatId, "🔎 Elimdekiler bitti — taze trend arıyorum…", ct: ct);
            try { await _scout.DiscoverTrendsAsync("TR", new[] { Platform.YouTube }, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "refresh: tarama başarısız"); }
            more = await RecentViralCandidatesAsync(shown, count, session.WithinHours, ct);
        }

        if (more.Count == 0)
        {
            await SendMessageAsync(session.ChatId, "😕 Şimdilik başka uygun aday kalmadı. Bir sonraki öneri partisini bekle.", ct: ct);
            return;
        }

        // Remember what we've now shown so the next refresh advances further.
        foreach (var t in more) shown.Add(t.Id);
        session.CandidateTrendIds = JsonSerializer.Serialize(shown);
        session.Stage = TelegramFlowStage.AwaitingSelection;
        await _db.SaveChangesAsync(ct);

        await SendCandidatesAsync(session.ChatId, more, isFirst: false, ct);
    }

    /// <summary>The candidates the autonomous flow sends: YouTube trends whose VIDEO was PUBLISHED within the
    /// last few hours (Telegram:CandidatePublishedWithinHours, default 3), ranked by virality — i.e. "what's
    /// going viral right now", not an all-time top list. <paramref name="exclude"/> drops already-shown ids
    /// (used by the refresh button). Falls back to top-by-virality only if nothing that fresh exists, so the
    /// flow never dead-ends.</summary>
    private async Task<List<Trend>> RecentViralCandidatesAsync(HashSet<int> exclude, int count, int withinHours, CancellationToken ct)
    {
        var publishedCutoff = DateTime.UtcNow.AddHours(-withinHours);
        var baseQuery = _db.Trends
            .Where(t => !t.IsProcessed && t.SourcePlatform == Platform.YouTube && !exclude.Contains(t.Id));

        var fresh = await baseQuery
            .Where(t => t.PublishedAt != null && t.PublishedAt >= publishedCutoff)
            .OrderByDescending(t => t.FinalViralityScore)
            .Take(count)
            .ToListAsync(ct);
        if (fresh.Count > 0) return fresh;

        // Nothing published that recently (e.g. a quiet window / scan gap) → don't dead-end: fall back to the
        // most viral unprocessed trends regardless of age.
        _logger.LogInformation("Son {H} saatte yayınlanmış aday yok — virallik sırasına göre seçiliyor.", withinHours);
        return await baseQuery.OrderByDescending(t => t.FinalViralityScore).Take(count).ToListAsync(ct);
    }

    // ── Callback state machine (called by the poll listener) ───────────────────────────────────
    public async Task HandleCallbackAsync(string chatId, string data, CancellationToken ct = default)
    {
        var split = data.Split(':', 2);
        var action = split[0];
        int.TryParse(split.Length > 1 ? split[1] : "", out var argId);

        // Find the active session by chat id WITHOUT the UserId filter (the poll listener has no logged-in
        // user), then pin the current user to the session's owner so keys + data scope are correct.
        var session = await _db.TelegramSessions
            .IgnoreQueryFilters()
            .Where(s => s.ChatId == chatId && s.IsActive)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
        if (session == null)
        {
            await SendMessageAsync(chatId, "Aktif bir oturum yok — bir sonraki öneri partisini bekle.", ct: ct);
            return;
        }
        _currentUser.Override(session.UserId);

        switch (action)
        {
            case "pick" when session.Stage is TelegramFlowStage.AwaitingSelection or TelegramFlowStage.ReviewingContent:
                session.SelectedTrendId = argId;
                session.Stage = TelegramFlowStage.Analyzing;
                await _db.SaveChangesAsync(ct);
                await SendMessageAsync(chatId, "🔎 Video izleniyor ve analiz ediliyor… (konu + prompt hazırlanıyor)", ct: ct);
                _jobs.Enqueue<TelegramConversationService>(s => s.AnalyzeSelectedAsync(session.Id, CancellationToken.None));
                break;

            case "change":
                session.Stage = TelegramFlowStage.AwaitingSelection;
                await _db.SaveChangesAsync(ct);
                await SendMessageAsync(chatId, "👆 Yukarıdaki adaylardan başka bir video seç.", ct: ct);
                break;

            case "refresh":
                await SendDifferentCandidatesAsync(session, ct);
                break;

            case "confirm":
                session.Stage = TelegramFlowStage.AwaitingToolChoice;
                await _db.SaveChangesAsync(ct);
                await SendToolChoiceAsync(chatId, session.VideoId ?? 0, ct);
                break;

            case "tool_pexels":
            case "tool_ai":
                session.VisualMode = action == "tool_ai" ? "AIGenerated" : "StockVideo";
                session.Stage = TelegramFlowStage.Generating;
                await _db.SaveChangesAsync(ct);
                await SendMessageAsync(chatId, $"⚙️ Video üretiliyor ({(action == "tool_ai" ? "AI" : "Pexels")})… bu biraz sürebilir.", ct: ct);
                _jobs.Enqueue<TelegramConversationService>(s => s.GenerateSelectedAsync(session.Id, CancellationToken.None));
                break;

            case "revideo":
                session.Stage = TelegramFlowStage.AwaitingToolChoice;
                await _db.SaveChangesAsync(ct);
                await SendMessageAsync(chatId, "🔄 Yeniden oluşturalım.", ct: ct);
                await SendToolChoiceAsync(chatId, session.VideoId ?? 0, ct);
                break;

            case "pub":
                session.Stage = TelegramFlowStage.Publishing;
                await _db.SaveChangesAsync(ct);
                await SendMessageAsync(chatId, "📤 YouTube'a yükleniyor…", ct: ct);
                _jobs.Enqueue<TelegramConversationService>(s => s.PublishSelectedAsync(session.Id, CancellationToken.None));
                break;

            default:
                _logger.LogWarning("Telegram akışı: bilinmeyen/uygunsuz aksiyon '{Action}' (stage={Stage})", action, session.Stage);
                break;
        }
    }

    // ── Heavy steps (Hangfire jobs) ────────────────────────────────────────────────────────────
    public async Task AnalyzeSelectedAsync(int sessionId, CancellationToken ct)
    {
        var session = await _db.TelegramSessions.FindAsync(new object[] { sessionId }, ct);
        if (session == null) return;
        _currentUser.Override(session.UserId);  // run as the session owner (correct keys + data scope)
        if (session.SelectedTrendId is not { } trendId) return;
        var trend = await _db.Trends.FindAsync(new object[] { trendId }, ct);
        if (trend == null) { await SendMessageAsync(session.ChatId, "Seçilen trend bulunamadı.", ct: ct); return; }

        try
        {
            var script = await _analyzer.AnalyzeAndGenerateScriptAsync(trend, null, ct);
            var video = script.Video ?? throw new InvalidOperationException("Senaryo bir Video'ya bağlanamadı.");
            session.VideoId = video.Id;
            session.Stage = TelegramFlowStage.ReviewingContent;
            await _db.SaveChangesAsync(ct);

            var scenes = await _db.Scenes.Where(s => s.VideoId == video.Id).OrderBy(s => s.OrderIndex).ToListAsync(ct);
            var kb = InlineKeyboard(
                new[] { Btn("✅ Bu videoyu üret", $"confirm:{video.Id}") },
                new[] { Btn("🔄 Başka video", "change:0") });
            await SendMessageAsync(session.ChatId, BuildContentReview(trend, scenes), kb, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram akışı: analiz başarısız (session {Id})", sessionId);
            session.Stage = TelegramFlowStage.AwaitingSelection;
            await _db.SaveChangesAsync(CancellationToken.None);
            if (IsQuotaError(ex))
            {
                // Keep the selection so a single "Tekrar dene" re-runs the same video once the quota resets.
                session.Stage = TelegramFlowStage.ReviewingContent;
                await _db.SaveChangesAsync(CancellationToken.None);
                var kb = InlineKeyboard(
                    new[] { Btn("🔁 Tekrar dene", $"pick:{trend.Id}") },
                    new[] { Btn("🔄 Başka video", "change:0") });
                await SendMessageAsync(session.ChatId, QuotaMessage, kb, ct: CancellationToken.None);
            }
            else
            {
                await SendMessageAsync(session.ChatId, $"⚠️ Bu video analiz edilemedi: {Trunc(ex.Message, 200)}\n👆 Başka bir video seç.", ct: CancellationToken.None);
            }
        }
    }

    public async Task GenerateSelectedAsync(int sessionId, CancellationToken ct)
    {
        var session = await _db.TelegramSessions.FindAsync(new object[] { sessionId }, ct);
        if (session == null) return;
        _currentUser.Override(session.UserId);  // run as the session owner (correct keys + data scope)
        if (session.VideoId is not { } videoId) return;

        var video = await _db.Videos.Include(v => v.Script).Include(v => v.Scenes)
            .FirstOrDefaultAsync(v => v.Id == videoId, ct);
        if (video?.Script == null) { await SendMessageAsync(session.ChatId, "Video/senaryo bulunamadı.", ct: ct); return; }

        try
        {
            if (!string.IsNullOrWhiteSpace(session.VisualMode) && Enum.TryParse<VisualMode>(session.VisualMode, true, out var vm))
                video.VisualMode = vm;

            var template = await _db.Templates.FirstOrDefaultAsync(t => t.Type == video.TemplateType, ct)
                ?? await _db.Templates.FirstOrDefaultAsync(t => t.IsDefault, ct)
                ?? throw new InvalidOperationException("Şablon bulunamadı.");

            // requestApproval: false — we send the video + our own review buttons below.
            var result = await _generator.GenerateVideoAsync(video.Script, template, ct, requestApproval: false);

            session.Stage = TelegramFlowStage.ReviewingVideo;
            await _db.SaveChangesAsync(ct);

            var kb = InlineKeyboard(
                new[] { Btn("✅ YouTube'a yükle", $"pub:{result.Id}") },
                new[] { Btn("🔄 Yeniden oluştur", $"revideo:{result.Id}") });

            if (!string.IsNullOrWhiteSpace(result.OutputFilePath) && File.Exists(result.OutputFilePath))
                await SendVideoFileAsync(session.ChatId, result.OutputFilePath,
                    $"🎬 {result.Title}\n⏱ {result.DurationSeconds}sn · {result.VisualMode}", kb, ct);
            else
                await SendMessageAsync(session.ChatId, "Video üretildi ama çıktı dosyası bulunamadı.", kb, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram akışı: üretim başarısız (session {Id})", sessionId);
            session.Stage = TelegramFlowStage.AwaitingToolChoice;
            await _db.SaveChangesAsync(CancellationToken.None);
            var kb = InlineKeyboard(new[] { Btn("🔄 Tekrar dene", $"revideo:{session.VideoId}") });
            var msg = IsQuotaError(ex) ? QuotaMessage : $"⚠️ Üretim başarısız: {Trunc(ex.Message, 200)}";
            await SendMessageAsync(session.ChatId, msg, kb, ct: CancellationToken.None);
        }
    }

    public async Task PublishSelectedAsync(int sessionId, CancellationToken ct)
    {
        var session = await _db.TelegramSessions.FindAsync(new object[] { sessionId }, ct);
        if (session == null) return;
        _currentUser.Override(session.UserId);  // run as the session owner (correct YouTube OAuth token + keys)
        if (session.VideoId is not { } videoId) return;
        var video = await _db.Videos.FindAsync(new object[] { videoId }, ct);
        if (video == null) return;

        try
        {
            var pub = await _publisher.PublishToPlatformAsync(video, Platform.YouTube, ct);
            session.Stage = TelegramFlowStage.Completed;
            session.IsActive = false;
            await _db.SaveChangesAsync(ct);
            await SendMessageAsync(session.ChatId, $"✅ YouTube'a yüklendi!\n{pub.PlatformUrl}", ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram akışı: yayın başarısız (session {Id})", sessionId);
            session.Stage = TelegramFlowStage.ReviewingVideo;
            await _db.SaveChangesAsync(CancellationToken.None);
            var kb = InlineKeyboard(new[] { Btn("🔄 Tekrar dene", $"pub:{video.Id}") });
            var msg = ChainContains(ex, "yetkilendirme")
                ? "⚠️ YouTube yetkilendirmesi yapılmamış.\nBir kez tarayıcıdan izin ver → `POST /api/youtube/authorize` (aynı makinede tarayıcı açılır, YouTube hesabınla onayla).\nSonra tekrar '✅ YouTube'a yükle'ye bas 👇"
                : $"⚠️ YouTube'a yükleme başarısız: {Trunc(ex.Message, 200)}";
            await SendMessageAsync(session.ChatId, msg, kb, ct: CancellationToken.None);
        }
    }

    // ── Message builders ───────────────────────────────────────────────────────────────────────
    private async Task SendToolChoiceAsync(string chatId, int videoId, CancellationToken ct)
    {
        var kb = InlineKeyboard(
            new[] { Btn("🎬 Pexels (hazır stok video)", $"tool_pexels:{videoId}") },
            new[] { Btn("🎨 AI üretimi", $"tool_ai:{videoId}") });
        await SendMessageAsync(chatId, "🛠 Video hangi araçla üretilsin?", kb, ct);
    }

    private static string BuildContentReview(Trend trend, List<Scene> scenes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🎬 {trend.Title}").AppendLine();
        sb.AppendLine($"📌 Konu: {trend.ContentSummary ?? "(çıkarılamadı)"}");
        if (trend.AiFeasibilityScore is { } score)
            sb.AppendLine($"🤖 AI uygunluk: {score}/100 — {trend.AiFeasibilityReason}");
        sb.AppendLine().AppendLine("🎞 Sahneler & prompt'lar:");
        foreach (var s in scenes)
        {
            sb.AppendLine($"• [{s.OrderIndex + 1}] {Trunc(s.NarrationText, 110)}");
            if (!string.IsNullOrWhiteSpace(s.VideoPrompt))
                sb.AppendLine($"   🎥 {Trunc(s.VideoPrompt, 150)}");
        }
        var text = sb.ToString();
        return text.Length <= 3900 ? text : text[..3900] + "…";
    }

    // ── Telegram HTTP helpers (absolute URLs — token's ':' breaks relative-path resolution) ──────
    private async Task<long?> SendMessageAsync(string chatId, string text, object? replyMarkup = null, CancellationToken ct = default)
    {
        var token = _secrets.TelegramBotToken;
        if (string.IsNullOrWhiteSpace(token)) return null;

        var payload = new Dictionary<string, object> { ["chat_id"] = chatId, ["text"] = text };
        if (replyMarkup != null) payload["reply_markup"] = replyMarkup;

        try
        {
            var client = _http.CreateClient("Telegram");
            var resp = await client.PostAsync($"https://api.telegram.org/bot{token}/sendMessage",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram sendMessage {Status}: {Body}", resp.StatusCode, Trunc(json, 200));
                return null;
            }
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("message_id", out var m))
                return m.GetInt64();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Telegram sendMessage başarısız");
        }
        return null;
    }

    private async Task SendVideoFileAsync(string chatId, string filePath, string caption, object? replyMarkup, CancellationToken ct)
    {
        var token = _secrets.TelegramBotToken;
        if (string.IsNullOrWhiteSpace(token)) return;

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(chatId), "chat_id" },
                { new StringContent(caption.Length > 1024 ? caption[..1024] : caption), "caption" },
            };
            if (replyMarkup != null)
                form.Add(new StringContent(JsonSerializer.Serialize(replyMarkup)), "reply_markup");

            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            var vc = new ByteArrayContent(bytes);
            vc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
            form.Add(vc, "video", Path.GetFileName(filePath));

            var client = _http.CreateClient("Telegram");
            var resp = await client.PostAsync($"https://api.telegram.org/bot{token}/sendVideo", form, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Telegram sendVideo {Status}", resp.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Telegram sendVideo başarısız");
        }
    }

    private static object InlineKeyboard(params object[][] rows) => new { inline_keyboard = rows };
    private static object Btn(string text, string data) => new { text, callback_data = data };
    private static string Trunc(string? s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..n] + "…";

    /// <summary>Detects a Gemini rate-limit / quota-exhausted failure (all keys cooled down) so the flow can
    /// show a clear "kota doldu, sonra dene" message instead of a raw 429 dump.</summary>
    private static bool IsQuotaError(Exception ex)
    {
        var m = ex.Message;
        return m.Contains("429") || m.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase)
            || m.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || m.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || m.Contains("Tüm Gemini", StringComparison.OrdinalIgnoreCase); // key-pool "all keys exhausted" message
    }

    private const string QuotaMessage =
        "⏳ Gemini kotası şu an dolu (ücretsiz hız sınırı). Birkaç dakika bekleyip tekrar dene 👇";

    /// <summary>Walks the InnerException chain (this codebase wraps caught exceptions, preserving the cause)
    /// looking for a substring — so error-type detection isn't fooled by an outer wrapper message.</summary>
    private static bool ChainContains(Exception ex, string text)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (e.Message.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
