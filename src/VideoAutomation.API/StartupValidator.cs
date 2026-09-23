using VideoAutomation.Domain.Enums;
using VideoAutomation.Infrastructure.AI;

namespace VideoAutomation.API;

/// <summary>
/// Fail-fast config validation run at startup. Two levels:
/// ERRORS (structurally invalid config that would otherwise blow up mid-run with a cryptic message —
/// bad enum/cron/range, empty connection string) throw and stop the app;
/// WARNINGS (missing optional API keys) are logged so the user immediately sees which features are off,
/// but the app still boots (you can run the panel and scan YouTube without, say, a Telegram token).
/// </summary>
public static class StartupValidator
{
    public static void Validate(IConfiguration config, ILogger logger)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // ── Structural / must-be-valid-if-present ──
        if (string.IsNullOrWhiteSpace(config.GetConnectionString("DefaultConnection")))
            errors.Add("ConnectionStrings:DefaultConnection boş — veritabanı bağlantısı yok.");

        var cron = config["Pipeline:CronSchedule"];
        if (!string.IsNullOrWhiteSpace(cron) && !LooksLikeCron(cron))
            errors.Add($"Pipeline:CronSchedule geçersiz görünüyor: '{cron}' (5 alanlı cron bekleniyor, ör. '0 9 * * *').");

        var visualMode = config["Pipeline:DefaultVisualMode"];
        if (!string.IsNullOrWhiteSpace(visualMode) && !Enum.TryParse<VisualMode>(visualMode, ignoreCase: true, out _))
            errors.Add($"Pipeline:DefaultVisualMode geçersiz: '{visualMode}' (StockVideo / AIGenerated / Mixed).");

        var minScore = config.GetValue<int?>("Pipeline:MinAiFeasibilityScore");
        if (minScore is < 0 or > 100)
            errors.Add($"Pipeline:MinAiFeasibilityScore 0-100 aralığında olmalı (şu an: {minScore}).");

        RequirePositive(config, "Video:Width", errors);
        RequirePositive(config, "Video:Height", errors);
        RequirePositive(config, "Scraping:MaxResultsPerPlatform", errors);

        // API keys are per-user now (entered in the UI, stored encrypted) — there are no global keys to
        // validate at startup. Each service reports a clear "panelden gir" error if the logged-in user
        // hasn't configured the key it needs.
        var secretsMasterKey = config["Security:EncryptionMasterKey"];
        if (string.IsNullOrWhiteSpace(secretsMasterKey) || secretsMasterKey.StartsWith("your", StringComparison.OrdinalIgnoreCase))
            warnings.Add("ENCRYPTION_MASTER_KEY ayarlı değil/placeholder — kullanıcı API anahtarları güvenli saklanamaz.");

        foreach (var w in warnings)
            logger.LogWarning("⚠️  Config: {Warning}", w);

        if (errors.Count > 0)
        {
            var msg = "Başlangıç config doğrulaması BAŞARISIZ:\n  - " + string.Join("\n  - ", errors);
            logger.LogCritical("{Message}", msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation("✅ Config doğrulandı ({WarnCount} uyarı) — API anahtarları kullanıcı başına.", warnings.Count);
    }

    private static void RequirePositive(IConfiguration config, string key, List<string> errors)
    {
        var v = config.GetValue<int?>(key);
        if (v is <= 0)
            errors.Add($"{key} pozitif olmalı (şu an: {v}).");
    }

    // Lightweight structural check (5 standard fields). Hangfire does the real parse when the recurring job
    // is registered right after; this just gives a clear early message for the common typo case.
    private static bool LooksLikeCron(string cron)
    {
        var fields = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length is 5 or 6;
    }
}
