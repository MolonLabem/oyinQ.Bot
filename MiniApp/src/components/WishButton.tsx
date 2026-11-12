import { useEffect, useRef, useState } from "react";
import { api, json } from "../api/client";
import { useAsync } from "../hooks/useAsync";
import { Notice } from "./Ui";
import { wishlistCopy } from "../app/productCopy";
import { successEventName } from "../telegram/webApp";

export function WishButton({ communityKey, bggId, initial, changed }: {
  communityKey: string; bggId: number; initial?: boolean; changed?: () => void;
}) {
  const state = useAsync(() => initial === undefined
    ? api<{ wished: boolean }>(`/wishlist/${bggId}?community=${encodeURIComponent(communityKey)}`)
    : Promise.resolve({ wished: initial }), [communityKey, bggId, initial]);
  const [value, setValue] = useState<boolean>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const scope = useRef(0);
  useEffect(() => { setValue(undefined); }, [initial]);
  useEffect(() => {
    scope.current++; setValue(undefined); setError(undefined); setBusy(false);
    return () => { scope.current++; };
  }, [communityKey, bggId]);
  const wished = value ?? state.data?.wished ?? initial ?? false;
  async function toggle() {
    const version = scope.current;
    setBusy(true); setError(undefined);
    try {
      const result = await api<{ wished: boolean }>(`/wishlist/${bggId}`, json("PUT", { communityKey, wished: !wished }));
      if (version !== scope.current) return;
      setValue(result.wished); changed?.(); window.dispatchEvent(new Event(successEventName));
    } catch (e) { if (version === scope.current) setError(e instanceof Error ? e.message : String(e)); }
    finally { if (version === scope.current) setBusy(false); }
  }
  return <div><button type="button" className="wishlist-button" aria-pressed={wished} disabled={busy || state.loading || !!state.error} onClick={() => void toggle()}>
    {busy ? "Сохраняем…" : wished ? wishlistCopy.selected : wishlistCopy.want}</button>
    {(error || state.error) && <Notice kind="danger">{error || state.error}</Notice>}</div>;
}
