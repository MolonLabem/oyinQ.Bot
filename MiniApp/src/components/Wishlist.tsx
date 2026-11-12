import { useState } from "react";
import { api } from "../api/client";
import type { CatalogResponse, ClubGame, Community } from "../api/types";
import { useAsync } from "../hooks/useAsync";
import { GamePicker } from "./GamePicker";
import { Card, Empty, ErrorState, Loading } from "./Ui";
import { ComplexityBadge } from "./ComplexityBadge";
import { GameCard } from "./GameCard";
import { GameDetail } from "../pages/games/GameDetail";
import { WishButton } from "./WishButton";
import { wishlistCopy } from "../app/productCopy";
export { WishButton } from "./WishButton";

export function WishlistPanel({ community, bggAvailable }: { community: Community; bggAvailable: boolean }) {
  const state = useAsync(() => api<CatalogResponse>(`/catalog?community=${encodeURIComponent(community.key)}&ownership=wishes`), [community.key]);
  const games = useAsync(() => api<ClubGame[]>(`/games?community=${encodeURIComponent(community.key)}`), [community.key]);
  const [selected, setSelected] = useState<ClubGame>();
  const [detail, setDetail] = useState<number>();
  return <><div hidden={detail !== undefined}><section className="wishlist-panel" aria-label={wishlistCopy.title}>
    <GamePicker selectionMode="wish" catalog={games.data} catalogLoading={games.loading} catalogError={games.error} bggAvailable={bggAvailable}
      selected={selected} onSelect={game => setSelected(game)} onClear={() => setSelected(undefined)} label={wishlistCopy.add} />
    {selected && <div className="row"><strong>{selected.name}</strong><ComplexityBadge info={selected.complexityInfo} /><WishButton key={`${community.key}-${selected.bggId}`} communityKey={community.key} bggId={selected.bggId} changed={state.reload} /></div>}
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.items.length
      ? <Empty>{wishlistCopy.empty}</Empty>
      : <div className="stack">{state.data.items.map(game => <Card className="wish-card" key={game.bggId}><GameCard embedded game={game} context="wishlist" open={() => setDetail(game.bggId)} />
        <WishButton communityKey={community.key} bggId={game.bggId} initial={game.isWished} changed={state.reload} /></Card>)}</div>}
  </section></div>{detail !== undefined && <GameDetail community={community} bggId={detail} back={() => setDetail(undefined)} />}</>;
}
