using System.Collections.Concurrent;

namespace VideoAutomation.Infrastructure.AI;

/// <summary>
/// A pool of Gemini API keys with quota-aware failover. When a key returns 429 (free-tier rate/daily
/// limit), it's put in a short cooldown and callers rotate to the next available key — so a single
/// exhausted key doesn't block the whole app while others still have budget. Registered as a singleton so
/// cooldown state is shared across all requests.
/// </summary>
public class GeminiKeyPool
{
    private readonly List<string> _keys;
    // key -> UTC time it becomes usable again (absent = available now).
    private readonly ConcurrentDictionary<string, DateTime> _cooldownUntil = new();

    public GeminiKeyPool(IEnumerable<string> keys)
    {
        _keys = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct()
            .ToList();
    }

    /// <summary>How many distinct keys were configured.</summary>
    public int Count => _keys.Count;

    /// <summary>
    /// The keys to try for a request, ordered so keys NOT in cooldown come first, then the rest by soonest
    /// cooldown expiry (so if every key is cooling down we still attempt the one closest to being ready).
    /// A snapshot: the caller iterates it, reporting any that 429 so later requests skip them.
    /// </summary>
    public IReadOnlyList<string> KeysToTry()
    {
        var now = DateTime.UtcNow;
        return _keys
            .OrderBy(k => IsCoolingDown(k, now) ? 1 : 0)
            .ThenBy(k => _cooldownUntil.TryGetValue(k, out var until) ? until : DateTime.MinValue)
            .ToList();
    }

    /// <summary>Marks a key as quota-exhausted for <paramref name="cooldown"/> before it's retried.</summary>
    public void ReportQuotaExhausted(string key, TimeSpan cooldown)
    {
        _cooldownUntil[key] = DateTime.UtcNow.Add(cooldown);
    }

    /// <summary>Number of keys currently usable (not in cooldown) — surfaced in the settings status.</summary>
    public int AvailableCount()
    {
        var now = DateTime.UtcNow;
        return _keys.Count(k => !IsCoolingDown(k, now));
    }

    private bool IsCoolingDown(string key, DateTime now) =>
        _cooldownUntil.TryGetValue(key, out var until) && until > now;
}
