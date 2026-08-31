import { useCallback, useEffect, useRef, useState } from "react";

export function useAsync<T>(loader: () => Promise<T>, dependencies: unknown[]) {
  const [data, setData] = useState<T>(); const [error, setError] = useState<string>(); const [loading, setLoading] = useState(true);
  const requestVersion = useRef(0);
  const reload = useCallback(() => {
    const version = ++requestVersion.current;
    setLoading(true); setError(undefined);
    loader()
      .then(value => { if (version === requestVersion.current) setData(value); })
      .catch(e => { if (version === requestVersion.current) setError(e instanceof Error ? e.message : String(e)); })
      .finally(() => { if (version === requestVersion.current) setLoading(false); });
  }, dependencies);
  useEffect(() => {
    reload();
    return () => { requestVersion.current++; };
  }, [reload]);
  return { data, error, loading, reload, setData };
}

export function useDebouncedValue<T>(value: T, delayMilliseconds: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delayMilliseconds);
    return () => window.clearTimeout(timer);
  }, [value, delayMilliseconds]);
  return debounced;
}
