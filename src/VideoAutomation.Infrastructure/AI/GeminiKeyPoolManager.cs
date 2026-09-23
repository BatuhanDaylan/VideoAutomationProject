using System.Collections.Concurrent;

namespace VideoAutomation.Infrastructure.AI;

/// <summary>
/// Hands out a per-user <see cref="GeminiKeyPool"/> (singleton, so each user's 429/403 cooldown state
/// survives across requests). The pool is rebuilt only when that user's key set actually changes — so
/// updating keys in the UI takes effect immediately without losing other users' cooldown state.
/// </summary>
public class GeminiKeyPoolManager
{
    private readonly ConcurrentDictionary<int, (string Signature, GeminiKeyPool Pool)> _pools = new();

    public GeminiKeyPool GetPool(int userId, IReadOnlyList<string> keys)
    {
        var signature = string.Join("|", keys);
        return _pools.AddOrUpdate(
            userId,
            _ => (signature, new GeminiKeyPool(keys)),
            (_, existing) => existing.Signature == signature ? existing : (signature, new GeminiKeyPool(keys))
        ).Pool;
    }
}
