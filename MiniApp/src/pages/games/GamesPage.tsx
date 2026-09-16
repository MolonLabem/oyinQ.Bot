import { CommunityDemand } from "./CommunityDemand";
import { GameGatherings } from "./GameGatherings";
import { ComplexityBadge, ComplexityDetails } from "../../components/ComplexityBadge";
import { wishlistCopy } from "../../app/productCopy";
import { WishButton, WishlistPanel } from "../../components/Wishlist";
import { useEffect, useState } from "react";
import { ApiError, api } from "../../api/client";
import type { CatalogResponse, Community, GameDetails, GameListItem } from "../../api/types";
import { BackButton, Badge, ContactLink, Cover, Empty, ErrorState, Field, Loading, Notice, Page, Tabs } from "../../components/Ui";
import { useAsync, useDebouncedValue } from "../../hooks/useAsync";
import { successEventName, telegram } from "../../telegram/webApp";
import { activeGroups, catalogParams, emptyFilters, filterChips, filterStorageKey, gameCount, normalizeFilters, restoreFilters, type FilterOptions } from "../../app/catalogFilters";
import { CatalogFilters } from "./CatalogFilters";
import { collectionMissingMessage } from "../../app/collectionNavigation";
import { GameTaxonomy } from "../../components/GameTaxonomy";

type GamesPageProps = { createGathering?: (id: number) => void; openGathering?: (id: string) => void; community: Community; bggAvailable: boolean; initialGameId?: number; onInitialConsumed?: () => void; backToGathering?: () => void };
export function GamesPage(props: GamesPageProps) {
  return <CommunityGames key={`${props.community.mode}:${props.community.key}`} {...props} />;
}
function CommunityGames({ community, bggAvailable, initialGameId, onInitialConsumed, backToGathering, createGathering, openGathering }: GamesPageProps) {
  const [section, setSection] = useState("catalog");
  const [query, setQuery] = useState("");
  const [applied, setApplied] = useState(() => restoreFilters(community.key, community.mode));
  const [options, setOptions] = useState<FilterOptions>();
  const [sort, setSort] = useState("name");
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [selected, setSelected] = useState<number | undefined>(initialGameId);
  const [initialBackToGathering] = useState<(() => void) | undefined>(() => initialGameId ? backToGathering : undefined);
  useEffect(() => { if (initialGameId) onInitialConsumed?.(); }, []);
  const debouncedQuery = useDebouncedValue(query, 400);
  const params = catalogParams(community.key, community.mode, applied, debouncedQuery, sort);
  const state = useAsync(async () => ({ params, result: await api<CatalogResponse>(`/catalog?${params}`) }), [params, section, selected]);
  const result = state.data?.params === params ? state.data.result : undefined;
  useEffect(() => {
    if (!result || state.loading || state.error) return;
    setOptions(result.filters);
    setApplied(current => {
      const normalized = normalizeFilters(current, community.mode, result.filters);
      return JSON.stringify(normalized) === JSON.stringify(current) ? current : normalized;
    });
  }, [result, state.loading, state.error, community.mode]);
  useEffect(() => {
    try { sessionStorage.setItem(filterStorageKey(community.key, community.mode), JSON.stringify(applied)); } catch { /* Storage is optional. */ }
  }, [applied, community.key, community.mode]);
  useEffect(() => {
    const refresh = () => { if (document.visibilityState !== "hidden") state.reload(); };
    window.addEventListener("focus", refresh);
    window.addEventListener(successEventName, refresh);
    document.addEventListener("visibilitychange", refresh);
    return () => { window.removeEventListener("focus", refresh); window.removeEventListener(successEventName, refresh); document.removeEventListener("visibilitychange", refresh); };
  }, [state.reload]);
  const chips = filterChips(applied, options);
  const count = activeGroups(applied);
  const loading = state.loading || (!result && !state.error) || query !== debouncedQuery;
  const reset = () => { setApplied(emptyFilters()); setQuery(""); };
  if (selected) return <GameDetail createGathering={createGathering} openGathering={openGathering} attendanceDate={applied.attendanceDate} community={community} bggId={selected} back={() => { setSelected(undefined); initialBackToGathering?.(); }} />;
  return <Page title="Игры" subtitle={community.name}>
    <Tabs label="Разделы игр" active={section} onChange={setSection} items={[{ id: "catalog", label: "Каталог" }, { id: "wishlist", label: wishlistCopy.title }]} />
    {section === "wishlist" ? <><WishlistPanel community={community} bggAvailable={bggAvailable} /><CommunityDemand communityKey={community.key} create={createGathering} /></> : <>
      <div className="catalog-browse-toolbar">
        <Field label="Поиск игры"><input type="search" value={query} onChange={e => setQuery(e.target.value)} placeholder="Название игры или дополнения" /></Field>
        <div className="catalog-browse-actions"><Field label="Сортировка"><select value={sort} onChange={e => setSort(e.target.value)}><option value="name">По названию</option><option value="players">По числу игроков</option><option value="popular">По сыгранным партиям</option></select></Field>
        <button type="button" disabled={!options} aria-expanded={filtersOpen} aria-controls="catalog-filters" className={count ? "filter-button active" : "filter-button"} onClick={() => setFiltersOpen(true)}>Фильтры{count ? ` · ${count}` : ""}</button></div>
      </div>
      {chips.length > 0 && <div className="catalog-applied"><div className="catalog-chip-strip" aria-label="Активные фильтры">{chips.map(chip => <button type="button" className="filter-chip" key={chip.key} aria-label={`Убрать фильтр: ${chip.label}`} onClick={() => setApplied(chip.remove)}><span>{chip.label}</span><span aria-hidden>×</span></button>)}</div><button className="ghost" type="button" onClick={() => setApplied(emptyFilters())}>Сбросить</button></div>}
      <p className="catalog-result-status" role="status" aria-live="polite">{loading ? "Обновляем список игр…" : state.error ? "Не удалось обновить игры" : gameCount(result?.total ?? result?.items.length ?? 0)}</p>
      {loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !result?.items.length ? <Empty>{query.trim() || count ? <>Ничего не найдено. Попробуйте изменить запрос или фильтры.<button type="button" onClick={reset}>Сбросить поиск и фильтры</button></> : community.mode === "Club" ? "В коллекции пока нет игр. Администратор может добавить их в разделе управления коллекцией." : "На кэмпе пока нет игр. Добавьте свою игру в разделе «Профиль → Моя коллекция»."}</Empty> : <CatalogGameList items={result.items} searching={Boolean(debouncedQuery.trim())} open={setSelected} />}
      {filtersOpen && options && <CatalogFilters community={community} applied={applied} options={options} search={debouncedQuery} sort={sort} onClose={() => setFiltersOpen(false)} onApply={value => { if (catalogParams(community.key, community.mode, value, debouncedQuery, sort) === params) state.reload(); setApplied(value); setFiltersOpen(false); }} />}
    </>}
  </Page>;
}

export function CatalogGameList({ items, searching = false, open }: { items: GameListItem[]; searching?: boolean; open: (id: number) => void }) {
  const nestedIds = new Set(items.flatMap(game => (game.expansions ?? []).filter(exp => exp.bggId !== game.bggId).map(exp => exp.bggId)));
  const availableIds = new Set(items.map(game => game.bggId));
  return <div className="catalog-grid">{items.filter(game => !nestedIds.has(game.bggId)).map(game => <div className={`catalog-game-group${game.expansions?.length ? " has-expansions" : ""}`} key={game.bggId}>
    <GameCard game={game} open={() => open(game.bggId)} />
    {Boolean(game.expansions?.length) && <details className="collection-expansions" open={searching ? true : undefined}>
      <summary><span><strong>Дополнения</strong><small>Часть этой карточки</small></span><Badge tone="accent">{game.expansions!.length}</Badge></summary>
      <ul className="catalog-expansion-list">{game.expansions!.map(exp => <li key={exp.bggId}>{availableIds.has(exp.bggId)
        ? <button className="catalog-expansion-option" onClick={() => open(exp.bggId)}><span aria-hidden>↳</span><span>{exp.name} <ComplexityBadge info={exp.complexityInfo} /></span><small>Открыть</small></button>
        : <div className="catalog-expansion-option static"><span aria-hidden>↳</span><span>{exp.name} <ComplexityBadge info={exp.complexityInfo} /></span><small>Дополнение</small></div>}</li>)}</ul>
    </details>}
  </div>)}</div>;
}

function GameCard({ game, open }: { game: GameListItem; open: () => void }) {
  return <button className="card catalog-card" onClick={open}><Cover src={game.thumbnailImageUrl} name={game.name} /><span className="catalog-card-body"><strong className="catalog-title">{game.name}</strong><ComplexityBadge info={game.complexityInfo} /><span className="catalog-card-facts">{game.minPlayers && game.maxPlayers && <small><span aria-hidden>👥</span> {game.minPlayers}–{game.maxPlayers}{game.bestPlayers ? ` · лучше ${game.bestPlayers}` : ""}</small>}{game.expansionPlayerRange && <small>С дополнениями: {game.expansionPlayerRange.minimum}–{game.expansionPlayerRange.maximum}</small>}{Boolean(game.scheduledGatherings) && <small className="catalog-activity">Запланировано: {game.scheduledGatherings}</small>}</span>{game.availabilitySummary && <small className={game.needsProviderCoordination ? "availability warning" : game.isDefinitelyAvailable ? "availability success" : "availability"}>{game.availabilitySummary}</small>}</span></button>;
}

function GameDetail({ community, bggId, attendanceDate, back, createGathering, openGathering }: { createGathering?: (id: number) => void; openGathering?: (id: string) => void; community: Community; bggId: number; attendanceDate?: string; back: () => void }) {
  const state = useAsync(() => api<GameDetails>(`/catalog/${bggId}?community=${encodeURIComponent(community.key)}${attendanceDate ? `&attendanceDate=${encodeURIComponent(attendanceDate)}` : ""}`), [community.key, bggId, attendanceDate]);
  useEffect(() => telegram.back(true, back), [back]);
  if (state.loading) return <Page title="Игра"><Loading /></Page>;
  if (state.failure instanceof ApiError && state.failure.code === "game_not_in_collection") return <Page title="Игра" subtitle={community.name} actions={<BackButton onClick={back} />}><Notice kind="warning">{collectionMissingMessage(community)}</Notice></Page>;
  if (state.error || !state.data) return <Page title="Игра" actions={<BackButton onClick={back} />}><ErrorState message={state.error ?? "Игра не найдена"} retry={state.reload} /></Page>;
  const game = state.data; const time = game.minPlayTimeMinutes ? game.minPlayTimeMinutes === game.maxPlayTimeMinutes ? `${game.minPlayTimeMinutes} мин` : `${game.minPlayTimeMinutes}–${game.maxPlayTimeMinutes ?? "?"} мин` : undefined;
  return <Page title={game.name} subtitle={game.yearPublished ? `${game.yearPublished} год` : undefined} actions={<BackButton onClick={back} />}>
    <div className="game-detail-hero"><Cover src={game.imageUrl} name={game.name} /><div><div className="detail-facts">{game.minPlayers && game.maxPlayers && <span>👥 {game.minPlayers}–{game.maxPlayers}</span>}{game.expansionPlayerRange && <span>С дополнениями: {game.expansionPlayerRange.minimum}–{game.expansionPlayerRange.maximum}</span>}{game.bestPlayers && <span>Лучше: {game.bestPlayers}</span>}{time && <span>⏱ {time}</span>}{game.minAge != null && <span>{game.minAge}+</span>}</div><a className="button secondary-link" href={game.bggUrl} target="_blank" rel="noreferrer">Открыть на BGG</a></div></div>
    {game.canWish !== false && createGathering && <button className="primary" onClick={() => createGathering(bggId)}>Создать сбор по этой игре</button>}
    {game.canWish !== false && <WishButton key={bggId} communityKey={community.key} bggId={bggId} initial={game.isWished} />}
    <section className="content-section detail-section">{community.mode === "Club" ? <p className={`availability${game.availability.isInBaseCollection || game.availability.isOwned ? " success" : ""}`}>Коробка · {game.availability.isInBaseCollection ? "Есть в клубе" : "Нет в коллекции клуба"}{game.availability.isOwned && " · Есть у вас"}</p> : <><h2>Кто привезёт игру{attendanceDate ? ` · ${attendanceDate}` : ""}</h2>{game.availability.isOwned && <Notice kind="success">Есть у вас</Notice>}{game.availability.isInBaseCollection && <Notice kind="success">✓ Есть в коллекции клуба</Notice>}{game.availability.providers.length > 0 && <><h3>Кто может привезти</h3><ul className="provider-list">{game.availability.providers.map(provider => <li key={provider.participantId}><span>{provider.commitment === "Bringing" ? "✓ " : ""}<ContactLink url={provider.contactUrl}>{provider.displayName}</ContactLink>{provider.city ? ` (${provider.city})` : ""}</span><span className={`provider-status${provider.commitment === "Bringing" ? " success" : ""}`}>{provider.commitment === "Bringing" ? "точно привезёт" : "может привезти"}</span></li>)}</ul></>}{!game.availability.isInBaseCollection && !game.availability.hasCommittedProvider && game.availability.providers.length === 0 && <Notice kind="warning">Пока никто не подтвердил коробку. Сбор всё равно можно создать.</Notice>}{!game.availability.isInBaseCollection && !game.availability.hasCommittedProvider && game.availability.providers.length > 1 && <Notice kind="warning">Нужно решить, кто привезёт игру.</Notice>}</>}</section>
    <section className="content-section detail-section game-activity"><h2>Активность</h2><div><span><strong>{game.scheduledGatherings ?? 0}</strong><small>запланировано сборов</small></span><span><strong>{game.recordedPlays ?? 0}</strong><small>подтверждено партий</small></span></div></section>
    {openGathering && <GameGatherings communityKey={community.key} bggId={bggId} open={openGathering} />}
    {game.description && <details className="content-section detail-section detail-disclosure"><summary>Об игре</summary><div>{game.description.split("\n").map((text, i) => text ? <p key={i}>{text}</p> : null)}</div></details>}
    <ComplexityBadge info={game.complexityInfo} /><ComplexityDetails info={game.complexityInfo} />
    <GameTaxonomy typeNames={game.typeNames} categoryNames={game.categories.map(item => item.name)} mechanicNames={game.mechanics.map(item => item.name)} />
    {game.expansions.length > 0 && <details className="content-section detail-section detail-disclosure"><summary>Дополнения в коллекции ({game.expansions.length})</summary><ul>{game.expansions.map(item => <li key={item.bggId}>{item.name}</li>)}</ul></details>}
  </Page>;
}
