import { useMemo, useState } from "react";
import { api } from "../../api/client";
import type { ClubGame, Community } from "../../api/types";
import { Card, Cover, Empty, ErrorState, Field, Loading, Page } from "../../components/Ui";
import { GameMeta, searchGames } from "../../components/GamePicker";
import { useAsync } from "../../hooks/useAsync";

type CatalogGame = ClubGame & { copyCount?: number; providers?: { displayName: string }[] };

export function GamesPage({ community }: { community: Community }) {
  const [query, setQuery] = useState("");
  const state = useAsync(() => community.mode === "Camp"
    ? api<CatalogGame[]>(`/camp/catalog?community=${encodeURIComponent(community.key)}`)
    : api<CatalogGame[]>(`/games?community=${encodeURIComponent(community.key)}`), [community.key, community.mode]);
  const games = useMemo(() => searchGames(state.data ?? [], query), [state.data, query]);
  return <Page title="Игры" subtitle={community.name}>
    <Field label="Поиск"><input type="search" value={query} onChange={e => setQuery(e.target.value)} placeholder="Начните вводить название" /></Field>
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !games.length ? <Empty>{query ? "Игры не найдены. Попробуйте сократить запрос." : "Каталог пока пуст."}</Empty> : <div className="game-list">{games.map(game => <Card key={game.bggId}><div className="media"><Cover src={game.thumbnailImageUrl} name={game.name} /><div><h2>{game.name}</h2><GameMeta game={game} />{game.copyCount && <p>{game.copyCount} коп.</p>}{game.providers?.length ? <ul className="providers">{game.providers.map((p, i) => <li key={`${p.displayName}-${i}`}>{p.displayName}</li>)}</ul> : null}{game.expansions?.length ? <p className="muted">Дополнения: {game.expansions.map(e => e.name).join(", ")}</p> : null}</div></div></Card>)}</div>}
  </Page>;
}
