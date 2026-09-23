using VideoAutomation.Infrastructure.Scrapers;

namespace VideoAutomation.Tests;

/// <summary>
/// LanguageHeuristics.IsTurkish is a deliberately heuristic filter (see its own doc-comment). These lock
/// the behaviors it was written to get right: a platform-reported code always wins, Turkish-only chars and
/// word hits count, Indonesian/English false positives are rejected, and ç/ö/ü do NOT by themselves imply
/// Turkish.
/// </summary>
public class LanguageHeuristicsTests
{
    [Theory]
    // Platform-reported code wins over the text's own signals, both ways:
    [InlineData("The Man Is Exposed Government School", "tr", true)]
    [InlineData("keşfet komedi güzel", "en", false)]
    [InlineData("herhangi bir metin", "tr-TR", true)]
    // No platform code → fall back to text signals:
    [InlineData("bu çok güzel bir video", null, true)]        // Turkish filler words
    [InlineData("Işık ve gölge oyunu", null, true)]           // ı + ş (Turkish-only chars)
    [InlineData("The Man Is Exposed Madhya Pradesh Government School", null, false)] // English
    [InlineData("Belajar Alfabet dengan Visual Gambar", null, false)]               // Indonesian
    [InlineData("IMO NIH BOS SENGGOL DONG komedi lucu", null, false)]               // Indonesian > 1 Turkish hit
    [InlineData("Über Ökologie und Grün", null, false)]       // German: ö/ü are NOT treated as Turkish-only
    // Non-Latin scripts are decisive on their own:
    [InlineData("こんにちは世界", null, false)]                 // CJK/Hiragana
    [InlineData("मध्य प्रदेश सरकार", null, false)]              // Devanagari
    // Empty/whitespace:
    [InlineData(null, null, false)]
    [InlineData("   ", null, false)]
    public void IsTurkish_Cases(string? text, string? platformLanguageCode, bool expected)
    {
        Assert.Equal(expected, LanguageHeuristics.IsTurkish(text, platformLanguageCode));
    }

    [Fact]
    public void WholeWordMatch_DoesNotFireInsideLongerWord()
    {
        // "ne" (a Turkish word) must not fire inside "one"/"generation", "the" not inside "thermal" —
        // this English string has zero standalone Turkish words, so it must be rejected.
        Assert.False(LanguageHeuristics.IsTurkish("thermal generation zone", null));
    }
}
