import { useMemo, useState } from "react";
import { plural } from "../../app/format";
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
  const subtitle = state.data ? `${community.name} · ${games.length} из ${state.data.length}` : community.name;

  return <Page title="Игры" subtitle={subtitle}>
    <div className="catalog-search"><Field label="Поиск"><input type="search" value={query}
      onChange={event => setQuery(event.target.value)} placeholder="Название, категория или тип" /></Field></div>
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} />
      : !games.length ? <Empty>{query ? "Игры не найдены. Попробуйте сократить запрос." : "Каталог пока пуст."}</Empty>
      : <div className="catalog-grid">{games.map(game => {
        const hasDetails = Boolean(game.providers?.length || game.expansions?.length);
        return <Card className="catalog-card" key={game.bggId}>
          <Cover src={game.thumbnailImageUrl} name={game.name} />
          <div className="catalog-card-body">
            <h2 title={game.name}>{game.name}</h2>
            <GameMeta game={game} compact />
            {(game.copyCount || game.providers?.length || game.expansions?.length) && <div className="catalog-facts">
              {game.copyCount ? <span>{plural(game.copyCount, "копия", "копии", "копий")}</span> : null}
              {game.providers?.length ? <span>{plural(game.providers.length, "владелец", "владельца", "владельцев")}</span> : null}
              {game.expansions?.length ? <span>{plural(game.expansions.length, "дополнение", "дополнения", "дополнений")}</span> : null}
            </div>}
            {hasDetails && <details className="catalog-more"><summary>Подробнее</summary>
              {game.providers?.length ? <p><strong>Привезут:</strong> {game.providers.map(provider => provider.displayName).join(", ")}</p> : null}
              {game.expansions?.length ? <p><strong>Дополнения:</strong> {game.expansions.map(expansion => expansion.name).join(", ")}</p> : null}
            </details>}
          </div>
        </Card>;
      })}</div>}
  </Page>;
}
