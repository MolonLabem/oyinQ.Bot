import { useEffect, useRef, useState } from "react";
import { api, json } from "../api/client";
import { telegram } from "../telegram/webApp";

export function useCampWishlistAction(key: string) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  async function act(body: object) {
    if (busy) return false; setBusy(true); setError(undefined);
    try { await api(`/camp-wishlist?community=${encodeURIComponent(key)}`, json("POST", body)); if (alive.current) telegram.success("Сохранено"); return true; }
    catch (e) { if (alive.current) setError(e instanceof Error ? e.message : String(e)); return false; }
    finally { if (alive.current) setBusy(false); }
  }
  return { act, busy, error };
}
