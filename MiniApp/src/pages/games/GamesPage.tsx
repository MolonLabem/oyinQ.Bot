import { useEffect, useMemo, useState } from "react";
import { api } from "../../api/client";
import type { CatalogResponse, Community, GameDetails, GameListItem, GameType } from "../../api/types";
import { Badge, Card, Cover, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";

export function GamesPage({ community }: { community: Community }) {
  const [query, setQuery] = useState(""); const [players, setPlayers] = useState<number>();
  const [types, setTypes] = useState<GameType[]>([]); const [categories, setCategories] = useState<number[]>([]);
  const [sort, setSort] = useState("name"); const [filtersOpen, setFiltersOpen] = useState(false); const [selected, setSelected] = useState<number>();
  const params = useMemo(() => { const p = new URLSearchParams({ community: community.key, sort }); if (query.trim()) p.set("search", query.trim()); if (players) p.set("players", String(players)); if (types.length) p.set("types", types.join(",")); if (categories.length) p.set("categories", categories.join(",")); return p.toString(); }, [community.key, query, players, types, categories, sort]);
  const state = useAsync(() => api<CatalogResponse>(`/catalog?${params}`), [params]);
  const activeFilters = (players ? 1 : 0) + types.length + categories.length;
  if (selected) return <GameDetail community={community} bggId={selected} back={() => setSelected(undefined)} />;
  return <Page title="Игры" subtitle={state.data ? `${community.name} · ${state.data.items.length}` : community.name}>
    <div className="catalog-toolbar"><Field label="Поиск"><input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Название игры" /></Field><button className={activeFilters ? "filter-button active" : "filter-button"} onClick={() => setFiltersOpen(value => !value)}>Фильтры{activeFilters ? ` · ${activeFilters}` : ""}</button></div>
    {filtersOpen && <Card className="filter-panel"><div className="filter-group"><strong>Можно играть в составе</strong><div className="choice-row">{[1,2,3,4,5,6].map(value => <button className={players === value ? "active" : ""} key={value} onClick={() => setPlayers(players === value ? undefined : value)}>{value}</button>)}</div><Field label="Точное количество игроков"><input type="number" min="1" inputMode="numeric" value={players ?? ""} onChange={event => setPlayers(event.target.value ? Math.max(1, Number(event.target.value)) : undefined)} /></Field></div><FilterChecks title="Тип" values={state.data?.filters.types ?? []} selected={types} toggle={value => setTypes(toggle(types, value))} /><FilterChecks title="Категории" values={(state.data?.filters.categories ?? []).map(value => ({ key: value.bggId, value: value.name }))} selected={categories} toggle={value => setCategories(toggle(categories, value))} /><Field label="Сортировка"><select value={sort} onChange={event => setSort(event.target.value)}><option value="name">Название</option><option value="players">Количество игроков</option></select></Field>{activeFilters > 0 && <button className="ghost" onClick={() => { setPlayers(undefined); setTypes([]); setCategories([]); }}>Сбросить фильтры</button>}</Card>}
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.items.length ? <Empty>Игры не найдены. Измените поиск или фильтры.</Empty> : <div className="catalog-grid">{state.data.items.map(game => <GameCard key={game.bggId} game={game} open={() => setSelected(game.bggId)} />)}</div>}
  </Page>;
}

function FilterChecks<T extends string | number>({ title, values, selected, toggle: change }: { title: string; values: { key: T; value: string }[]; selected: T[]; toggle: (value: T) => void }) {
  if (!values.length) return null;
  return <fieldset className="filter-group"><legend>{title}</legend><div className="filter-checks">{values.map(item => <label className="check" key={item.key}><input type="checkbox" checked={selected.includes(item.key)} onChange={() => change(item.key)} />{item.value}</label>)}</div></fieldset>;
}
function toggle<T>(values: T[], value: T) { return values.includes(value) ? values.filter(item => item !== value) : [...values, value]; }

function GameCard({ game, open }: { game: GameListItem; open: () => void }) {
  return <button className="card catalog-card" onClick={open}><Cover src={game.thumbnailImageUrl} name={game.name} /><span className="catalog-card-body"><strong className="catalog-title">{game.name}</strong><Badge tone="accent">{game.typeName}</Badge>{game.minPlayers && game.maxPlayers && <small>👥 {game.minPlayers}–{game.maxPlayers}{game.bestPlayers ? ` · лучше ${game.bestPlayers}` : ""}</small>}{game.availabilitySummary && <small className={game.needsProviderCoordination ? "availability warning" : game.isDefinitelyAvailable ? "availability success" : "availability"}>{game.availabilitySummary}</small>}</span></button>;
}

function GameDetail({ community, bggId, back }: { community: Community; bggId: number; back: () => void }) {
  const state = useAsync(() => api<GameDetails>(`/catalog/${bggId}?community=${encodeURIComponent(community.key)}`), [community.key, bggId]);
  useEffect(() => telegram.back(true, back), [back]);
  if (state.loading) return <Page title="Игра"><Loading /></Page>;
  if (state.error || !state.data) return <Page title="Игра" actions={<button onClick={back}>Назад</button>}><ErrorState message={state.error ?? "Игра не найдена"} retry={state.reload} /></Page>;
  const game = state.data; const time = game.minPlayTimeMinutes ? game.minPlayTimeMinutes === game.maxPlayTimeMinutes ? `${game.minPlayTimeMinutes} мин` : `${game.minPlayTimeMinutes}–${game.maxPlayTimeMinutes ?? "?"} мин` : undefined;
  return <Page title={game.name} subtitle={game.yearPublished ? `${game.typeName} · ${game.yearPublished}` : game.typeName} actions={<button onClick={back}>Назад</button>}><div className="game-detail-hero"><Cover src={game.imageUrl} name={game.name} /><div><div className="detail-facts">{game.minPlayers && game.maxPlayers && <span>👥 {game.minPlayers}–{game.maxPlayers}</span>}{game.bestPlayers && <span>Лучше: {game.bestPlayers}</span>}{time && <span>⏱ {time}</span>}{game.minAge != null && <span>{game.minAge}+</span>}</div><a className="button primary-link" href={game.bggUrl} target="_blank" rel="noreferrer">Открыть на BGG</a></div></div>{game.description && <Card className="detail-section"><h2>Об игре</h2>{game.description.split("\n").map((text, i) => text ? <p key={i}>{text}</p> : null)}</Card>}<Card className="detail-section"><h2>Доступность</h2>{game.availability.isInBaseCollection && <Notice kind="success">✓ Коллекция клуба</Notice>}{game.availability.providers.length > 0 && <><h3>Кто может привезти</h3><ul className="provider-list">{game.availability.providers.map(provider => <li key={provider.participantId}><span>{provider.commitment === "Bringing" ? "✅ " : ""}{provider.displayName}{provider.city ? ` (${provider.city})` : ""}</span><span>{provider.commitment === "Bringing" ? "точно привезёт" : "может привезти"}</span></li>)}</ul></>}{!game.availability.isInBaseCollection && !game.availability.hasCommittedProvider && game.availability.providers.length > 1 && <Notice kind="warning">Нужно решить, кто привезёт игру.</Notice>}</Card>{game.categories.length > 0 && <Card className="detail-section"><h2>Категории</h2><div className="tag-list">{game.categories.map(item => <span className="tag" key={item.bggId}>{item.name}</span>)}</div></Card>}{game.mechanics.length > 0 && <Card className="detail-section"><h2>Механики</h2><div className="tag-list">{game.mechanics.map(item => <span className="tag" key={item.bggId}>{item.name}</span>)}</div></Card>}{game.expansions.length > 0 && <Card className="detail-section"><h2>Дополнения в коллекции</h2><ul>{game.expansions.map(item => <li key={item.bggId}>{item.name}</li>)}</ul></Card>}</Page>;
}
