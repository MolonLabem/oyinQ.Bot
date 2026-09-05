import { useEffect, useRef, useState } from "react";
import { api, json } from "../api/client";
import type { CatalogResponse, ClubGame, Community } from "../api/types";
import { useAsync } from "../hooks/useAsync";
import { GamePicker } from "./GamePicker";
import { Card, Empty, ErrorState, Loading, Notice } from "./Ui";

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
      setValue(result.wished); changed?.();
    } catch (e) { if (version === scope.current) setError(e instanceof Error ? e.message : String(e)); }
    finally { if (version === scope.current) setBusy(false); }
  }
  return <div><button type="button" aria-pressed={wished} disabled={busy || state.loading || !!state.error} onClick={() => void toggle()}>
    {busy ? "Сохраняем…" : wished ? "♥ В вишлисте" : "♡ Хочу сыграть"}</button>
    {(error || state.error) && <Notice kind="danger">{error || state.error}</Notice>}</div>;
}

export function WishlistPanel({ community, bggAvailable }: { community: Community; bggAvailable: boolean }) {
  const state = useAsync(() => api<CatalogResponse>(`/catalog?community=${encodeURIComponent(community.key)}&ownership=wishes`), [community.key]);
  const games = useAsync(() => api<ClubGame[]>(`/games?community=${encodeURIComponent(community.key)}`), [community.key]);
  const [selected, setSelected] = useState<ClubGame>();
  return <Card><h2>Вишлист · {community.name}</h2>
    <p className="muted">Игры, в которые хочется сыграть в этом сообществе. Коробку иметь не обязательно. Запись в сбор и обещание привезти игру оформляются отдельно.</p>
    <GamePicker catalog={games.data} catalogLoading={games.loading} catalogError={games.error} bggAvailable={bggAvailable}
      selected={selected} onSelect={game => setSelected(game)} onClear={() => setSelected(undefined)} label="Добавить в вишлист" />
    {selected && <div className="row"><strong>{selected.name}</strong><WishButton key={`${community.key}-${selected.bggId}`} communityKey={community.key} bggId={selected.bggId} changed={state.reload} /></div>}
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.items.length
      ? <Empty>Вишлист пока пуст. Найдите игру выше.</Empty>
      : <ul className="provider-list">{state.data.items.map(game => <li key={game.bggId}><span>{game.name}</span>
        <WishButton communityKey={community.key} bggId={game.bggId} initial={game.isWished} changed={state.reload} /></li>)}</ul>}
  </Card>;
}
