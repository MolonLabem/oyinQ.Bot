import { useCallback, useEffect, useState } from "react";

export function useAsync<T>(loader: () => Promise<T>, dependencies: unknown[]) {
  const [data, setData] = useState<T>(); const [error, setError] = useState<string>(); const [loading, setLoading] = useState(true);
  const reload = useCallback(() => { setLoading(true); setError(undefined); loader().then(setData).catch(e => setError(e instanceof Error ? e.message : String(e))).finally(() => setLoading(false)); }, dependencies);
  useEffect(reload, [reload]);
  return { data, error, loading, reload, setData };
}
