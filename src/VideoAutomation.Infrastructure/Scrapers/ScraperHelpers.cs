using System.Text.Json;
using Microsoft.Extensions.Configuration;
using VideoAutomation.Domain.Enums;

namespace VideoAutomation.Infrastructure.Scrapers;

/// <summary>
/// Defensive JSON access and small utilities shared by the YouTube/TikTok/Instagram scrapers.
/// Field access is defensive on purpose: third-party scraper output schemas (Apify actors especially)
/// can rename or drop fields without notice, and one malformed item must never abort a whole scrape.
/// </summary>
internal static class ScraperHelpers
{
    public static string SafeGetString(JsonElement element, string propertyName, string defaultValue = "")
    {
        if (element.ValueKind != JsonValueKind.Object) return defaultValue;
        if (!element.TryGetProperty(propertyName, out var value)) return defaultValue;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? defaultValue,
            JsonValueKind.Number => value.ToString(),
            _ => defaultValue,
        };
    }

    public static long SafeGetInt64(JsonElement element, string propertyName, long defaultValue = 0)
    {
        if (element.ValueKind != JsonValueKind.Object) return defaultValue;
        if (!element.TryGetProperty(propertyName, out var value)) return defaultValue;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var l) => l,
            JsonValueKind.String when long.TryParse(value.GetString(), out var l) => l,
            _ => defaultValue,
        };
    }

    public static DateTime? SafeGetDateTime(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(propertyName, out var value)) return null;
        var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }

    /// <summary>
    /// Best-effort category inference: matches configured seed keywords against the trend's own text.
    /// A single scraper call returns a mix of categories, so each item is classified after the fact.
    /// </summary>
    public static TrendCategory InferCategory(string text, IReadOnlyDictionary<string, List<string>> seedsByCategory)
    {
        if (string.IsNullOrWhiteSpace(text)) return TrendCategory.Other;
        var lowered = text.ToLowerInvariant();

        foreach (var (categoryName, keywords) in seedsByCategory)
        {
            foreach (var keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword) && lowered.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return Enum.TryParse<TrendCategory>(categoryName, ignoreCase: true, out var category)
                        ? category
                        : TrendCategory.Other;
                }
            }
        }

        return TrendCategory.Other;
    }

    /// <summary>Apify requires the '/' in "owner/actor-name" to become '~' in the run-sync URL path.</summary>
    public static string EncodeApifyActorId(string actorId) => actorId.Replace('/', '~');

    /// <summary>Reads Scraping:SeedHashtagsByCategory (keyed by exact TrendCategory enum names) from config.</summary>
    public static Dictionary<string, List<string>> LoadSeedHashtagsByCategory(IConfiguration configuration)
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var categorySection in configuration.GetSection("Scraping:SeedHashtagsByCategory").GetChildren())
        {
            var keywords = categorySection.GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList();
            if (keywords.Count > 0) result[categorySection.Key] = keywords;
        }
        return result;
    }

    /// <summary>Flattens all configured seed hashtags into one deduplicated list, capped at maxCount.</summary>
    public static List<string> FlattenSeedHashtags(IReadOnlyDictionary<string, List<string>> seedsByCategory, int maxCount)
        => seedsByCategory.Values.SelectMany(v => v).Distinct(StringComparer.OrdinalIgnoreCase).Take(maxCount).ToList();
}
