using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;
using VideoAutomation.Shared.Exceptions;

namespace VideoAutomation.Infrastructure.AI;

/// <summary>
/// IContentGenerator implementation backed by Gemini. Two independent capabilities:
/// AnalyzeTrendAsync (trend analiz servisi) and GenerateScriptAsync (viral trendin gerçek içeriğini —
/// transkript varsa transkriptten, yoksa başlık/açıklamadan — doğrudan uyarlayan, sabit olmayan sayıda
/// sahneli senaryo üretimi + sahne bazlı JSON çıktı formatı) — GenerateScriptAsync calls AnalyzeTrendAsync
/// internally to feed the analysis into the script prompt, so both checklist items are exercised
/// by a real code path rather than living side by side unused.
/// </summary>
public class GeminiContentGenerator : IContentGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Shared by GenerateScriptAsync (source = a Trend) and GenerateScriptFromIdeaAsync (source = free text /
    // a chat transcript): both want the exact same scene schema and audio/videoPrompt rules, so the
    // instruction lives in one place. The prompt each builds differs; this system persona does not.
    private const string ScriptSystemInstruction =
        "Sen viral kısa video (TikTok/Reels/Shorts) içerik uyarlama uzmanısın. Görevin, sana verilen " +
        "içeriğin (viral bir trendin gerçek içeriği ya da bir kullanıcının video fikri) özünü koruyarak onu " +
        "doğrudan Türkçe dikey kısa video formatına uyarlamaktır. Alakasız yeni bir kurgu, metafor ya da " +
        "yapay bir 'hook/twist' kalıbı uydurma. Kaynak metin (transkript/fikir) verilmişse narrationText'leri " +
        "doğrudan ondan türet (gerekirse kısalt/sadeleştir ama anlamını ve sırasını değiştirme). İçeriği, " +
        "istenen video uzunluğuna ve kendi doğal akışına göre DEĞİŞKEN sayıda sahneye böl — sahne sayısı " +
        "sabit DEĞİLDİR, hedef süreye göre belirlenir (her sahne yaklaşık 4-10 saniye; en az 2 sahne). " +
        "Her sahne için: (1) akıcı bir anlatım metni (narrationText), (2) Pexels'te aratılacak iki farklı " +
        "görsel anahtar kelime (visualKeywordA/B, A/B split için, İngilizce, 2-3 kelime), (3) bir AI video " +
        "üretim modeline verilecek İngilizce, detaylı/sinematik bir 'videoPrompt' üretirsin (kamera açısı, " +
        "ortam, hareket, ışık gibi somut görsel detaylar içermeli — sadece anahtar kelime değil, tam bir " +
        "sahne tarifi).\n" +
        "ÖNEMLİ — videoPrompt'ta SES de tarif edilmeli: model videoyu kendi sesiyle birlikte üretecek. " +
        "Ortam sesi, arka plandaki insan konuşmaları, doğal efektler yaz (ör. 'ambient city traffic and " +
        "distant chatter', 'quiet room tone with soft clinking of a cup'). Karakterler konuşacaksa " +
        "repliklerini İngilizce tırnak içinde ver (ör. a man says: \"I can't believe this\"). " +
        "ASLA anlatıcı/dış ses (narrator, voiceover) isteme — anlatım bu videolarda kullanılmıyor, " +
        "sahne kendi doğal sesiyle anlatılmalı.\n" +
        "sceneType alanına o sahnenin içerikteki rolünü kısaca tanımlayan serbest bir etiket yaz " +
        "(ör. 'giriş', 'gelişme', 'punchline', 'sonuç') — sabit bir kalıp değil, içeriğe göre değişir. " +
        "Ayrıca videonun tamamı için kısa, vurucu, merak uyandıran Türkçe bir 'title' üret (≤90 karakter, " +
        "YouTube'da yayınlanacak başlık; kaynak videonun başlığını kopyalama, aşırı clickbait yapma).\n" +
        "Yanıtını SADECE aşağıdaki JSON şemasında ver, başka hiçbir açıklama, markdown fence veya metin ekleme:\n" +
        """
        {"title":"...","scenes":[{"sceneType":"...","narrationText":"...","visualKeywordA":"...","visualKeywordB":"...","videoPrompt":"...","durationSeconds":5}]}
        """;

    private readonly GeminiClient _geminiClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiContentGenerator> _logger;

    public GeminiContentGenerator(GeminiClient geminiClient, IConfiguration configuration, ILogger<GeminiContentGenerator> logger)
    {
        _geminiClient = geminiClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> AnalyzeTrendAsync(Trend trend, CancellationToken ct = default)
    {
        var temperature = _configuration.GetValue("AI:AnalysisTemperature", 0.4);

        const string systemInstruction =
            "Sen deneyimli bir sosyal medya trend analisti ve içerik stratejistisin. " +
            "Verilen trendi kısa ve öz şekilde analiz edersin: neden viral olduğunu, hedef kitlesini ve " +
            "kısa video (TikTok/Reels/Shorts) için hangi anlatım açısının en güçlü olacağını belirtirsin. " +
            "Yanıtın 2-3 cümlelik düz metin olmalı, madde işareti veya JSON kullanma.";

        var prompt =
            $"""
            Trend: {trend.Title}
            Açıklama: {trend.Description}
            Kategori: {trend.Category}
            Kaynak platform: {trend.SourcePlatform}
            İzlenme: {trend.ViewCount:N0}, Beğeni: {trend.LikeCount:N0}, Yorum: {trend.CommentCount:N0}

            Bu trendi yukarıdaki talimata göre analiz et.
            """;

        try
        {
            var analysis = await _geminiClient.GenerateContentAsync(
                prompt, systemInstruction, temperature, maxOutputTokens: 512, ct: ct);

            if (string.IsNullOrWhiteSpace(analysis))
                throw new AnalysisException("Gemini boş analiz döndürdü.");

            return analysis.Trim();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Trend analysis failed for trend {TrendId}", trend.Id);
            throw new AnalysisException(ex.Message, ex);
        }
    }

    public async Task<YouTubeAnalysis?> AnalyzeYouTubeVideoAsync(string youtubeUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(youtubeUrl)) return null;

        var temperature = _configuration.GetValue("AI:AnalysisTemperature", 0.4);

        const string systemInstruction =
            "Sen hem video içerik analisti hem de yapay zeka video üretim fizibilite uzmanısın. Sana verilen " +
            "kısa videoyu İZLE ve TEK yanıtta iki iş yap:\n" +
            "1) İÇERİK (contentSummary): Videoda GERÇEKTEN ne olduğunu anlat — görselde ne var (kişiler, mekan, " +
            "nesneler, eylemler ve sırası), ses/konuşma varsa ne söyleniyor, videonun esprisi/mesajı ne. " +
            "Tahmin/uydurma yok, sadece gördüğünü/duyduğunu yaz. 4-6 cümle Türkçe.\n" +
            "2) FİZİBİLİTE (feasibilityScore): Bu içerik SADECE metinden-videoya yapay zeka klipleri + Türkçe " +
            "seslendirme ile yeniden üretilebilir mi? 0-100 puan. DÜŞÜK (0-40): belirli gerçek bir kişinin " +
            "yüzü/sesi, gerçek olay kaydı, belirli bir şarkı/dans, oyun içi görüntü, ekrandaki yazı/yorum " +
            "okuma. YÜKSEK (70-100): anlatı/bilgi/liste, genel sahnelerle canlandırılabilen günlük durumlar, " +
            "doğa/nesne/mekan. Tek cümlelik Türkçe gerekçe (feasibilityReason).\n" +
            "Yanıtını SADECE şu JSON şemasında ver, başka metin veya markdown ekleme:\n" +
            """
            {"contentSummary":"4-6 cümlelik Türkçe özet", "feasibilityScore": 0-100 arası tam sayı, "feasibilityReason":"tek cümle Türkçe gerekçe"}
            """;

        const string prompt =
            "Bu videoyu izle: içerikte gerçekten ne olduğunu anlat ve yapay zeka ile yeniden üretilebilirliğini puanla.";

        string raw;
        try
        {
            // 8192 (was 4096): JSON output + Gemini 3.x thinking tokens can still truncate at 4096, which
            // returns null and silently drops konu+uygunluk. Give it full headroom.
            raw = await _geminiClient.GenerateFromYouTubeAsync(
                youtubeUrl, prompt, systemInstruction, temperature, maxOutputTokens: 8192, ct: ct);
        }
        catch (InvalidOperationException ex)
        {
            // Optional enrichment: a failed/blocked/quota'd or unsupported video must not kill the analysis.
            _logger.LogWarning(ex, "YouTube video analizi başarısız: {Url}", youtubeUrl);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(GeminiClient.CleanJsonResponse(raw));
            var root = doc.RootElement;
            var summary = root.TryGetProperty("contentSummary", out var s) ? s.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(summary)) return null;
            var score = root.TryGetProperty("feasibilityScore", out var f) && f.TryGetInt32(out var fv)
                ? Math.Clamp(fv, 0, 100) : 0;
            var reason = root.TryGetProperty("feasibilityReason", out var r) ? r.GetString() ?? "" : "";
            return new YouTubeAnalysis(summary.Trim(), score, reason);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "YouTube analiz JSON ayrıştırılamadı. Raw: {Raw}", Truncate(raw, 300));
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<int, AiFeasibilityResult>> EvaluateAiFeasibilityAsync(
        IReadOnlyList<Trend> trends, CancellationToken ct = default)
    {
        var results = new Dictionary<int, AiFeasibilityResult>();
        if (trends.Count == 0) return results;

        var temperature = _configuration.GetValue("AI:AnalysisTemperature", 0.4);

        const string systemInstruction =
            "Sen bir yapay zeka video üretim fizibilite uzmanısın. Sana verilen viral içeriğin, SADECE " +
            "metinden-videoya (text-to-video) yapay zeka modeliyle üretilmiş klipler ve Türkçe seslendirme " +
            "kullanılarak yeniden üretilip üretilemeyeceğini değerlendirirsin. Gerçek arşiv görüntüsü, " +
            "gerçek kişilerin yüzü veya orijinal ses/müzik KULLANILAMAZ — her şey sıfırdan üretilecek.\n" +
            "DÜŞÜK puan ver (0-40) eğer içerik şunlara bağlıysa:\n" +
            "- Belirli gerçek bir kişinin yüzü, sesi veya kimliği (ünlüler, tanınmış YouTuber'lar, siyasetçiler)\n" +
            "- Gerçek bir olayın kaydı (haber görüntüsü, kaza anı, gerçek bir maç/konser)\n" +
            "- Belirli bir şarkı, dans akımı veya müzik performansı\n" +
            "- Oyun içi görüntü, ekran kaydı, uygulama arayüzü\n" +
            "- Ekrandaki yazıyı/yorumları okumaya dayalı format (caps, meme, yorum videosu)\n" +
            "YÜKSEK puan ver (70-100) eğer içerik şunlarsa:\n" +
            "- Anlatıma dayalı hikaye, bilgi, liste, açıklama\n" +
            "- Genel sahnelerle canlandırılabilecek günlük hayat durumları (kimliği önemsiz kişiler)\n" +
            "- Doğa, nesne, mekan, atmosfer ağırlıklı içerik\n" +
            "Sana numaralandırılmış bir içerik listesi verilir; HER BİRİ için ayrı puan üretirsin. " +
            "Yanıtını SADECE şu JSON şemasında ver, başka hiçbir metin ekleme:\n" +
            """
            {"results":[{"id": içeriğin id değeri, "score": 0-100 arası tam sayı, "reason": "tek cümlelik kısa Türkçe gerekçe"}]}
            """;

        var promptBuilder = new StringBuilder("Aşağıdaki içeriklerin her birini değerlendir:\n");
        foreach (var trend in trends)
        {
            promptBuilder.AppendLine($"\n--- id: {trend.Id} ---");
            promptBuilder.AppendLine($"Başlık: {trend.Title}");
            promptBuilder.AppendLine($"Açıklama: {Truncate(trend.Description, 300)}");
            promptBuilder.AppendLine($"Kategori: {trend.Category} | Platform: {trend.SourcePlatform}");
            var transcriptExcerpt = TrimTranscript(trend.Transcript);
            if (transcriptExcerpt != null)
                promptBuilder.AppendLine($"Transkript: {Truncate(transcriptExcerpt, 600)}");
        }

        string raw;
        try
        {
            // Generous token budget: Gemini 3.x spends "thinking" tokens out of this same allowance, and a
            // too-small limit truncates the JSON mid-string rather than erroring (live-hit at 256 tokens).
            var maxTokens = Math.Min(8192, 512 + trends.Count * 200);
            raw = await _geminiClient.GenerateContentAsync(promptBuilder.ToString(), systemInstruction, temperature, maxOutputTokens: maxTokens, ct: ct);
        }
        catch (InvalidOperationException ex)
        {
            // A blocked/failed sweep must not take the caller down — every trend just stays unevaluated.
            _logger.LogWarning(ex, "AI feasibility evaluation failed for {Count} trends", trends.Count);
            return results;
        }

        try
        {
            using var doc = JsonDocument.Parse(GeminiClient.CleanJsonResponse(raw));
            if (!doc.RootElement.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("AI feasibility response had no 'results' array. Raw: {Raw}", Truncate(raw, 300));
                return results;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;
                var score = item.TryGetProperty("score", out var s) && s.TryGetInt32(out var parsed)
                    ? Math.Clamp(parsed, 0, 100)
                    : 0;
                var reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                results[id] = new AiFeasibilityResult(score, reason);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "AI feasibility response was not valid JSON. Raw: {Raw}", Truncate(raw, 300));
        }

        return results;
    }

    private static string Truncate(string? text, int maxChars) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= maxChars ? text : text[..maxChars];

    public async Task<Script> GenerateScriptAsync(Trend trend, Template template, string language = "tr", CancellationToken ct = default)
    {
        var temperature = _configuration.GetValue("AI:ScriptTemperature", 0.7);

        // ContentSummary (what a model actually SAW when it watched the video) is the strongest content
        // source — use it as primary. Only spend a separate text "why viral" analysis call when we DON'T
        // have that video understanding (saves quota, and the understanding already covers the ground).
        var targetSeconds = _configuration.GetValue("Pipeline:TargetVideoSeconds", 35);
        var hasVideoUnderstanding = !string.IsNullOrWhiteSpace(trend.ContentSummary);
        var analysis = hasVideoUnderstanding ? null : await AnalyzeTrendAsync(trend, ct);
        var transcriptExcerpt = TrimTranscript(trend.Transcript);

        var contentBlock = new StringBuilder();
        if (hasVideoUnderstanding)
        {
            contentBlock.AppendLine("\nVideonun GERÇEK içeriği (bir yapay zeka videoyu İZLEYEREK çıkardı — ANA KAYNAK budur, senaryoyu buna dayandır):");
            contentBlock.AppendLine(trend.ContentSummary);
        }
        if (transcriptExcerpt != null)
        {
            contentBlock.AppendLine("\nVideo transkripti (konuşma metni — varsa repliklerde buna sadık kal):");
            contentBlock.AppendLine(transcriptExcerpt);
        }
        if (analysis != null)
        {
            contentBlock.AppendLine("\nTrend analizi:");
            contentBlock.AppendLine(analysis);
        }
        if (contentBlock.Length == 0)
            contentBlock.AppendLine("\nBu trend için video/transkript içeriği yok — başlık ve açıklamadaki bilgiye dayanarak uyarla.");

        var prompt =
            $"""
            Trend: {trend.Title}
            Açıklama: {trend.Description}
            Kategori: {trend.Category}
            {contentBlock}
            Video şablonu: {template.Name} ({template.Description})
            Anlatım dili: {language}

            Videonun toplam hedef uzunluğu yaklaşık {targetSeconds} saniye. İçeriği bu süreye yetecek kadar
            sahneye böl — her sahnenin durationSeconds değeri 4 ile 10 arasında olsun ve sahnelerin toplamı
            hedef süreye yakın olsun. Sahne sayısını hedef süreye ve içeriğin akışına göre belirle (sabit sayı
            yok; en az 2 sahne).
            """;

        string raw;
        try
        {
            // 8192 (was 2048): a multi-scene script with detailed English videoPrompts + Gemini 3.x thinking
            // tokens truncated the JSON mid-string at 2048 (live-hit: "$.scenes[2].videoPrompt ... end of data").
            raw = await _geminiClient.GenerateContentAsync(prompt, ScriptSystemInstruction, temperature, maxOutputTokens: 8192, ct: ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Script generation failed for trend {TrendId}", trend.Id);
            throw new AnalysisException(ex.Message, ex);
        }

        return ParseScriptResponse(raw, prompt, trend.Title, language);
    }

    public async Task<string> GenerateChatReplyAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct = default)
    {
        if (history.Count == 0)
            throw new AnalysisException("Sohbet geçmişi boş.");

        var temperature = _configuration.GetValue("AI:ChatTemperature", 0.85);

        const string systemInstruction =
            "Sen yaratıcı bir kısa video (TikTok/Reels/YouTube Shorts) fikir ve senaryo asistanısın. " +
            "Kullanıcıyla Türkçe, samimi ve pratik bir dille sohbet ederek dikey (9:16) kısa bir video " +
            "fikrini birlikte geliştirirsin: konusu, tonu, sahneleri, görsel atmosferi. Kısa ve net yanıt ver, " +
            "gerektiğinde somut öneriler sun ve eksik detayları sor. Kullanıcı hazır olduğunda fikri sahnelere " +
            "bölünmüş bir videoya dönüştürebileceğini hatırlat — ama JSON veya teknik format ÜRETME, o iş ayrı " +
            "bir adımda yapılıyor; sen sadece fikri konuşarak olgunlaştır.";

        var turns = history.Select(m => (m.Role, m.Content)).ToList();

        try
        {
            var reply = await _geminiClient.GenerateChatAsync(turns, systemInstruction, temperature, maxOutputTokens: 1024, ct: ct);
            if (string.IsNullOrWhiteSpace(reply))
                throw new AnalysisException("Gemini boş yanıt döndürdü.");
            return reply.Trim();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Chat reply generation failed");
            throw new AnalysisException(ex.Message, ex);
        }
    }

    public async Task<Script> GenerateScriptFromIdeaAsync(string idea, Template template, string language = "tr", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idea))
            throw new AnalysisException("Video fikri boş olamaz.");

        var temperature = _configuration.GetValue("AI:ScriptTemperature", 0.7);
        var targetSeconds = _configuration.GetValue("Pipeline:TargetVideoSeconds", 35);

        var prompt =
            $"""
            Aşağıda bir kullanıcının (gerekirse bir asistanla sohbetinden çıkan) kısa video fikri var.
            Bu fikri, yukarıdaki talimattaki JSON şemasına göre sahnelere bölünmüş bir Türkçe kısa video
            senaryosuna dönüştür. Fikrin kendi konusuna ve tonuna sadık kal; yeni ve alakasız bir kurgu uydurma.

            Video fikri / sohbet:
            {Truncate(idea, 4000)}

            Video şablonu: {template.Name} ({template.Description})
            Anlatım dili: {language}

            Videonun toplam hedef uzunluğu yaklaşık {targetSeconds} saniye. İçeriği bu süreye yetecek kadar
            sahneye böl — her sahnenin durationSeconds değeri 4 ile 10 arasında olsun ve sahnelerin toplamı
            hedef süreye yakın olsun. Sahne sayısını hedef süreye ve içeriğin akışına göre belirle (sabit sayı
            yok; en az 2 sahne).
            """;

        string raw;
        try
        {
            // 8192 (was 2048): a multi-scene script with detailed English videoPrompts + Gemini 3.x thinking
            // tokens truncated the JSON mid-string at 2048 (live-hit: "$.scenes[2].videoPrompt ... end of data").
            raw = await _geminiClient.GenerateContentAsync(prompt, ScriptSystemInstruction, temperature, maxOutputTokens: 8192, ct: ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Script generation from idea failed");
            throw new AnalysisException(ex.Message, ex);
        }

        var topic = Truncate(idea, 80).Replace("\n", " ").Trim();
        return ParseScriptResponse(raw, prompt, string.IsNullOrWhiteSpace(topic) ? "Sohbet videosu" : topic, language);
    }

    public async Task<ManualAnalysisResult> AnalyzeManualDescriptionAsync(string description, Template template, string language = "tr", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new AnalysisException("Video açıklaması boş olamaz.");

        var temperature = _configuration.GetValue("AI:ScriptTemperature", 0.7);
        var targetSeconds = _configuration.GetValue("Pipeline:TargetVideoSeconds", 35);

        const string systemInstruction =
            "Sen hem viral kısa video uyarlama uzmanı hem de yapay zeka video üretim fizibilite uzmanısın. " +
            "Sana bir kullanıcının izlediği kısa bir videonun ne hakkında olduğunu anlatan bir AÇIKLAMA " +
            "verilir. TEK yanıtta iki iş yaparsın:\n" +
            "1) FİZİBİLİTE: Bu içerik SADECE metinden-videoya yapay zeka klipleri + Türkçe seslendirme ile " +
            "yeniden üretilebilir mi? 0-100 puan. DÜŞÜK (0-40): belirli gerçek bir kişinin yüzü/sesi, gerçek " +
            "olay kaydı, belirli bir şarkı/dans, oyun içi görüntü, ekrandaki yazı/yorum okuma. YÜKSEK " +
            "(70-100): anlatı/bilgi/liste, genel sahnelerle canlandırılabilen günlük durumlar, doğa/nesne/" +
            "mekan ağırlıklı içerik. Tek cümlelik Türkçe gerekçe yaz.\n" +
            "2) SENARYO: Açıklamadaki gerçek içeriği koru, alakasız yeni bir kurgu uydurma; içeriği istenen " +
            "video uzunluğuna ve doğal akışına göre DEĞİŞKEN sayıda sahneye böl (her sahne 4-10 sn, en az 2; " +
            "sahne sayısı sabit değildir). Her sahne için: narrationText, iki İngilizce görsel anahtar " +
            "kelime (visualKeywordA/B), ve İngilizce detaylı/sinematik bir videoPrompt (SES de tarif et: " +
            "ortam sesi, arka plan konuşmaları; ASLA anlatıcı/voiceover isteme). sceneType serbest Türkçe " +
            "etiket. Ayrıca video için kısa, vurucu Türkçe bir 'title' üret (≤90 karakter, YouTube başlığı).\n" +
            "Yanıtını SADECE şu JSON şemasında ver, başka metin veya markdown ekleme:\n" +
            """
            {"feasibilityScore": 0-100 arası tam sayı, "feasibilityReason": "tek cümle Türkçe gerekçe", "title":"...", "scenes":[{"sceneType":"...","narrationText":"...","visualKeywordA":"...","visualKeywordB":"...","videoPrompt":"...","durationSeconds":5}]}
            """;

        var prompt =
            $"""
            Kullanıcının izlediği videonun açıklaması (ASIL içerik kaynağı — buna dayandır):
            {Truncate(description, 3000)}

            Video şablonu: {template.Name} ({template.Description})
            Anlatım dili: {language}

            Videonun toplam hedef uzunluğu yaklaşık {targetSeconds} saniye. İçeriği bu süreye yetecek kadar
            sahneye böl — her sahne 4-10 saniye, toplam hedef süreye yakın olsun. Sahne sayısını hedef süreye
            ve içeriğin akışına göre belirle (sabit sayı yok; en az 2 sahne).
            """;

        string raw;
        try
        {
            // Generous budget on purpose: this one call returns feasibility + a full multi-scene script with
            // detailed English videoPrompts, AND Gemini 3.x spends "thinking" tokens out of this same
            // allowance — 2048 truncated the JSON mid-string (live-hit). 8192 leaves ample headroom.
            raw = await _geminiClient.GenerateContentAsync(prompt, systemInstruction, temperature, maxOutputTokens: 8192, ct: ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Manuel analiz (fizibilite+senaryo) başarısız");
            throw new AnalysisException(ex.Message, ex);
        }

        var cleaned = GeminiClient.CleanJsonResponse(raw);
        int score;
        string reason;
        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            score = doc.RootElement.TryGetProperty("feasibilityScore", out var s) && s.TryGetInt32(out var sv)
                ? Math.Clamp(sv, 0, 100) : 0;
            reason = doc.RootElement.TryGetProperty("feasibilityReason", out var r) ? r.GetString() ?? "" : "";
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Manuel analiz JSON ayrıştırılamadı. Raw: {Raw}", Truncate(raw, 300));
            throw new AnalysisException($"Gemini geçersiz JSON döndürdü: {ex.Message}", ex);
        }

        // Scenes reuse the exact same GeminiScriptResponse shape; the extra feasibility* fields are ignored
        // by the deserializer, so ParseScriptResponse handles them transparently.
        var topic = Truncate(description, 80).Replace("\n", " ").Trim();
        var script = ParseScriptResponse(raw, prompt, string.IsNullOrWhiteSpace(topic) ? "Manuel video" : topic, language);

        return new ManualAnalysisResult(score, reason, script);
    }

    /// <summary>
    /// Shared parse of the scene-JSON both GenerateScriptAsync (from a Trend) and GenerateScriptFromIdeaAsync
    /// (from free text) produce — same schema (GeminiScriptResponse), same error handling, so the two entry
    /// points can't drift on how the model's JSON becomes a Script.
    /// </summary>
    private Script ParseScriptResponse(string raw, string prompt, string topic, string language)
    {
        var cleaned = GeminiClient.CleanJsonResponse(raw);

        GeminiScriptResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GeminiScriptResponse>(cleaned, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Gemini script JSON. Raw: {Raw}", raw);
            throw new AnalysisException($"Gemini geçersiz JSON döndürdü: {ex.Message}", ex);
        }

        if (parsed == null || parsed.Scenes.Count == 0)
            throw new AnalysisException("Gemini boş senaryo döndürdü.");

        return new Script
        {
            Topic = topic,
            FullText = string.Join("\n\n", parsed.Scenes.Select(s => s.NarrationText)),
            Language = language,
            GeminiPromptUsed = prompt,
            RawJsonResponse = cleaned,
        };
    }

    private string? TrimTranscript(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return null;
        var maxChars = _configuration.GetValue("Scraping:YouTubeTranscript:MaxCharsInPrompt", 3000);
        return transcript.Length <= maxChars ? transcript : transcript[..maxChars];
    }
}
