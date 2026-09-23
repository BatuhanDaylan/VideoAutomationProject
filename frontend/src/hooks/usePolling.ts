import { useCallback, useEffect, useRef, useState } from "react";

interface PollingResult<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
  refresh: () => void;
}

/**
 * Runs `fetcher` immediately and then every `intervalMs` — unless `intervalMs` is null, in which
 * case it fetches once and stops (used to pause polling when nothing is actively running). Keeps
 * the previous data visible across refreshes so the UI doesn't flash empty on every tick.
 */
export function usePolling<T>(
  fetcher: () => Promise<T>,
  intervalMs: number | null,
  deps: unknown[] = []
): PollingResult<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  const run = useCallback(async () => {
    try {
      const result = await fetcherRef.current();
      setData(result);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    run();
    if (intervalMs == null) return;
    const id = setInterval(run, intervalMs);
    return () => clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [intervalMs, run, ...deps]);

  return { data, error, loading, refresh: run };
}
