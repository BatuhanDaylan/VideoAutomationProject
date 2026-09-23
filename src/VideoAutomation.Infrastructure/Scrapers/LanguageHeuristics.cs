namespace VideoAutomation.Infrastructure.Scrapers;

/// <summary>
/// Best-effort "is this text Turkish?" detection for trend titles/descriptions.
/// Deliberately heuristic rather than a real language-detection library: trend titles are short, hashtag-
/// heavy, emoji-laden fragments ("#kesfet #komedi 😂") that general-purpose detectors handle poorly, and
/// pulling in a dependency for a 3-word string isn't worth it. The signals below were chosen against real
/// scan output (see the non-Turkish results this filter was written to reject: English "The Man Is Exposed
/// Madhya Pradesh Government School", Indonesian "Belajar Alfabet dengan Visual Gambar", etc.).
/// </summary>
public static class LanguageHeuristics
{
    /// <summary>
    /// ı/İ/ğ/Ğ/ş/Ş are near-exclusive to Turkish among the languages that actually show up in these scans.
    /// ç/ö/ü are deliberately NOT here — they're common in German/French/Azerbaijani and would misfire.
    /// </summary>
    private static readonly char[] TurkishOnlyChars = ['ı', 'İ', 'ğ', 'Ğ', 'ş', 'Ş'];

    private static readonly string[] TurkishWords =
    [
        "keşfet", "kesfet", "keşfetteyiz", "kesfetteyiz", "komedi", "komik", "mizah", "eğlence", "eglence",
        "için", "icin", "bir", "bu", "ve", "ile", "çok", "cok", "daha", "gibi", "ama", "sonra", "kadar",
        "var", "yok", "ne", "nasıl", "nasil", "neden", "kim", "sen", "ben", "biz", "siz", "onlar",
        "yapardın", "yapardin", "geldi", "oldu", "değil", "degil", "güzel", "guzel", "hayat", "aşk", "ask",
        "gün", "gun", "şaka", "saka", "türk", "turk", "türkiye", "turkiye", "izle", "video", "şov", "sov",
    ];

    /// <summary>
    /// Words that strongly indicate the text is NOT Turkish (Indonesian/Malay and English fillers).
    /// Indonesian is over-represented here on purpose: it's by far the most common false positive in
    /// TR-region Shorts results, and it shares vocabulary with Turkish ("komedi", "video"), so a Turkish
    /// hit alone can't outweigh it — the particle/filler words below are what actually disambiguate.
    /// </summary>
    private static readonly string[] NonTurkishWords =
    [
        // Indonesian/Malay content words
        "yang", "dengan", "untuk", "biar", "dapet", "banget", "aja", "gak", "nih", "kalo", "sama",
        "belajar", "adalah", "tidak", "sudah", "bisa", "orang", "anak", "lagi", "bikin", "kena",
        "lucu", "ngakak", "kocak", "receh", "emang", "udah", "kayak", "gitu", "jadi", "bos",
        // Indonesian particles — very high signal, near-absent in Turkish
        "dong", "deh", "sih", "mah", "kan", "kok",
        // English
        "the", "this", "that", "with", "when", "every", "will", "your", "from", "have", "about",
        "government", "school", "student", "update", "selection", "condition", "relatable", "funny",
    ];

    /// <summary>
    /// True when <paramref name="platformLanguageCode"/> (if the platform reported one) says Turkish, or —
    /// when it's absent — the text's own signals point to Turkish. A platform-reported code always wins:
    /// it comes from the uploader and beats guessing from a 5-word title.
    /// </summary>
    public static bool IsTurkish(string? text, string? platformLanguageCode = null)
    {
        if (!string.IsNullOrWhiteSpace(platformLanguageCode))
            return platformLanguageCode.StartsWith("tr", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(text)) return false;

        // Non-Latin scripts (Devanagari, Arabic, Cyrillic, CJK, Thai) — decisive on their own.
        if (text.Any(IsNonLatinScript)) return false;

        var lower = text.ToLowerInvariant();
        var turkishScore = text.Any(c => TurkishOnlyChars.Contains(c)) ? 2 : 0;
        turkishScore += TurkishWords.Count(w => ContainsWord(lower, w));
        var nonTurkishScore = NonTurkishWords.Count(w => ContainsWord(lower, w));

        return turkishScore > 0 && turkishScore >= nonTurkishScore;
    }

    /// <summary>Whole-word match, so "the" doesn't fire inside "thermal" and "ne" doesn't fire inside "one".</summary>
    private static bool ContainsWord(string haystack, string word)
    {
        var index = 0;
        while ((index = haystack.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(haystack[index - 1]);
            var afterIndex = index + word.Length;
            var afterOk = afterIndex >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);
            if (beforeOk && afterOk) return true;
            index = afterIndex;
        }

        return false;
    }

    private static bool IsNonLatinScript(char c) =>
        c is >= 'ऀ' and <= 'ॿ' or // Devanagari
        >= '؀' and <= 'ۿ' or      // Arabic
        >= 'Ѐ' and <= 'ӿ' or      // Cyrillic
        >= '一' and <= '鿿' or      // CJK
        >= '぀' and <= 'ヿ' or      // Hiragana/Katakana
        >= '฀' and <= '๿';        // Thai
}
