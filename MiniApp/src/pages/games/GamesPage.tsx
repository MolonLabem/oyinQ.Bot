import { CommunityDemand } from "./CommunityDemand";
import { CampWishlist } from "./CampWishlist";
import { GameDetail } from "./GameDetail";
import { ComplexityBadge } from "../../components/ComplexityBadge";
import { wishlistCopy } from "../../app/productCopy";
import { WishlistPanel } from "../../components/Wishlist";
import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../../api/client";
import type { CatalogResponse, Community, GameListItem } from "../../api/types";
import { Badge, Pagination, Empty, ErrorState, Loading, Page, Tabs } from "../../components/Ui";
import { useAsync, useDebouncedValue } from "../../hooks/useAsync";
import { successEventName } from "../../telegram/webApp";
import { activeGroups, catalogParams, emptyFilters, filterChips, filterStorageKey, gameCount, normalizeFilters, restoreFilters, type FilterOptions } from "../../app/catalogFilters";
import { CatalogFilters } from "./CatalogFilters";
import { plural } from "../../app/format";
import { catalogGroups } from "../../app/catalogPagination";
import { CloseIcon } from "../../components/CloseIcon";
import { GameCard } from "../../components/GameCard";
import { BrowseSearch } from "../../components/BrowseSearch";

type GamesPageProps = { createGathering?: (id: number) => void; openGathering?: (id: string) => void; community: Community; bggAvailable: boolean; initialGameId?: number; onInitialConsumed?: () => void; backToGathering?: () => void };
export function GamesPage(props: GamesPageProps) {
  return <CommunityGames key={`${props.community.mode}:${props.community.key}`} {...props} />;
}
function CommunityGames({ community, bggAvailable, initialGameId, onInitialConsumed, backToGathering, createGathering, openGathering }: GamesPageProps) {
  const [section, setSection] = useState(() => {
    if (new URLSearchParams(location.search).get("wishlist") === "1") return "wishlist";
    try { return sessionStorage.getItem(`oyinq-games-section:${community.key}`) ?? "catalog"; } catch { return "catalog"; }
  });
  useEffect(() => { try { sessionStorage.setItem(`oyinq-games-section:${community.key}`, section); } catch { /* Optional storage. */ } }, [section, community.key]);
  const [query, setQuery] = useState("");
  const [applied, setApplied] = useState(() => restoreFilters(community.key, community.mode));
  const [options, setOptions] = useState<FilterOptions>();
  const [sort, setSort] = useState("rating");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(() => {
    try { const saved = Number(sessionStorage.getItem("oyinq-catalog-page-size")); return [10, 20, 50].includes(saved) ? saved : 10; }
    catch { return 10; }
  });
  const results = useRef<HTMLElement>(null);
  const scrollToPage = useRef(false);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [selected, setSelected] = useState<number | undefined>(initialGameId);
  const [selectedPerson, setSelectedPerson] = useState<string>();
  const [initialBackToGathering] = useState<(() => void) | undefined>(() => initialGameId ? backToGathering : undefined);
  useEffect(() => { if (initialGameId) onInitialConsumed?.(); }, []);
  const debouncedQuery = useDebouncedValue(query, 400);
  const params = catalogParams(community.key, community.mode, applied, debouncedQuery, sort);
  const state = useAsync(async () => ({ params, result: await api<CatalogResponse>(`/catalog?${params}`) }), [params, section, selected]);
  const result = state.data?.params === params ? state.data.result : undefined;
  useEffect(() => {
    if (!result?.ratingsPending || selected || section !== "catalog") return;
    const timer = window.setTimeout(() => { if (document.visibilityState !== "hidden") void state.reload(); }, 5000);
    return () => window.clearTimeout(timer);
  }, [result, selected, section, state.reload]);
  const groups = useMemo(() => catalogGroups(result?.items ?? []), [result?.items]);
  const pageCount = Math.max(1, Math.ceil(groups.length / pageSize));
  const currentPage = Math.min(page, pageCount);
  useEffect(() => { setPage(1); }, [params]);
  useEffect(() => { if (result && !state.loading && page > pageCount) setPage(pageCount); }, [result, state.loading, page, pageCount]);
  useEffect(() => { try { sessionStorage.setItem("oyinq-catalog-page-size", String(pageSize)); } catch { /* Optional storage. */ } }, [pageSize]);
  useEffect(() => {
    if (!scrollToPage.current || !results.current) return;
    scrollToPage.current = false;
    results.current.scrollIntoView({ block: "start" });
    results.current.focus({ preventScroll: true });
  }, [page, pageSize]);
  const changePage = (value: number) => { scrollToPage.current = true; setPage(value); };
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
  // A same-query refresh keeps the mounted list (and expanded rows) in place.
  // Replacing it with Loading collapses the page and clamps the browser's scroll.
  const loading = (!result && !state.error) || query !== debouncedQuery;
  const reset = () => { setApplied(emptyFilters()); setQuery(""); };
  if (selectedPerson && community.mode === "Camp") return <Page title="Участник кэмпа" hideTitle><CampWishlist community={community} bggAvailable={bggAvailable} initialPersonId={selectedPerson} back={() => setSelectedPerson(undefined)} create={createGathering} openGathering={openGathering} /></Page>;
  if (selected && community.mode === "Camp" && section === "wishlist") return <Page title="Хотелки" hideTitle><CampWishlist community={community} bggAvailable={bggAvailable} initialGameId={selected} back={() => { setSelected(undefined); initialBackToGathering?.(); }} create={createGathering} openGathering={openGathering} /></Page>;
  if (selected) return <GameDetail createGathering={createGathering} openGathering={openGathering} attendanceDate={applied.attendanceDate} community={community} bggId={selected} back={() => { setSelected(undefined); initialBackToGathering?.(); }} openCampWishlist={() => setSection("wishlist")} openPerson={setSelectedPerson} />;
  return <div className={section === "catalog" ? "catalog-screen" : "games-wishes-screen"}><Page title="Игры" hideTitle>
    <Tabs label="Разделы игр" active={section} onChange={setSection} items={[{ id: "catalog", label: "Каталог" }, { id: "wishlist", label: wishlistCopy.title }]} />
    {section === "wishlist" ? community.mode === "Camp" ? <CampWishlist community={community} bggAvailable={bggAvailable} create={createGathering} openGathering={openGathering} /> : <><WishlistPanel community={community} bggAvailable={bggAvailable} /><CommunityDemand communityKey={community.key} create={createGathering} /></> : <>
      <div className="catalog-browse-toolbar">
        <BrowseSearch label="Поиск игры" value={query} onChange={setQuery} placeholder="Игра или дополнение" />
        <div className="catalog-browse-actions"><label className="catalog-sort"><span className="visually-hidden">Сортировка</span><select value={sort} onChange={e => setSort(e.target.value)}><option value="rating">Рейтинг BGG</option><option value="popular">Популярность</option><option value="name">По алфавиту</option></select></label>
        <button type="button" disabled={!options} aria-expanded={filtersOpen} aria-controls="catalog-filters" className={count ? "filter-button active" : "filter-button"} onClick={() => setFiltersOpen(true)}>Фильтры{count ? ` · ${count}` : ""}</button></div>
      </div>
      {chips.length > 0 && <div className="catalog-applied"><div className="catalog-chip-strip" aria-label="Активные фильтры">{chips.map(chip => <button type="button" className="filter-chip" key={chip.key} aria-label={`Убрать фильтр: ${chip.label}`} onClick={() => setApplied(chip.remove)}><span>{chip.label}</span><CloseIcon /></button>)}</div><button className="ghost" type="button" onClick={() => setApplied(emptyFilters())}>Сбросить</button></div>}
      {sort === "rating" && result?.ratingsPending && <p className="catalog-rating-status" role="status">Загружаем рейтинг BGG…</p>}
      {sort === "rating" && result?.ratingsUnavailable && !result.ratingsPending && <p className="catalog-rating-status" role="status">{result.items.some(game => game.bggRating != null) ? "Часть оценок BGG пока недоступна" : "Рейтинг BGG пока недоступен"}</p>}
      <section className="catalog-results" ref={results} tabIndex={-1} aria-label="Каталог игр">
      <div className="catalog-paging-options"><p className="catalog-result-status" role="status" aria-live="polite">{loading || state.loading ? "Обновляем список игр…" : state.error ? "Не удалось обновить игры" : gameCount(result?.total ?? result?.items.length ?? 0)}</p>{Boolean(groups.length) && <label>На странице<select aria-label="Игр на странице" value={pageSize} onChange={e => { scrollToPage.current = true; setPageSize(Number(e.target.value)); setPage(1); }}>{[10, 20, 50].map(size => <option key={size} value={size}>{size}</option>)}</select></label>}</div>
      {loading ? <Loading /> : result?.items.length ? <CatalogGameList items={result.items} offset={(currentPage - 1) * pageSize} limit={pageSize} searching={Boolean(debouncedQuery.trim())} open={setSelected} /> : !state.error && <Empty>{query.trim() || count ? <>Ничего не найдено. Попробуйте изменить запрос или фильтры.<button type="button" onClick={reset}>Сбросить поиск и фильтры</button></> : community.mode === "Club" ? "В коллекции пока нет игр. Попросите администратора добавить их." : <>На кэмпе пока нет игр. Добавьте свои в профиле и отметьте, что привезёте.<br /><a data-profile-nav href={`?community=${encodeURIComponent(community.key)}&tab=profile&profileTab=collection`}>Открыть «Игры» в профиле</a></>}</Empty>}
      {!loading && groups.length > 0 && <Pagination page={currentPage} hasPrevious={currentPage > 1} hasNext={currentPage < pageCount} onChange={changePage} label="Страницы каталога"><label className="catalog-page-select">Страница<select aria-label="Страница каталога" value={currentPage} onChange={e => changePage(Number(e.target.value))}>{Array.from({ length: pageCount }, (_, index) => <option value={index + 1} key={index + 1}>{index + 1}</option>)}</select><span>из {pageCount}</span></label></Pagination>}
      </section>
      {state.error && <ErrorState message={state.error} retry={state.reload} />}
      {filtersOpen && options && <CatalogFilters community={community} applied={applied} options={options} search={debouncedQuery} sort={sort} onClose={() => setFiltersOpen(false)} onApply={value => { if (catalogParams(community.key, community.mode, value, debouncedQuery, sort) === params) state.reload(); setApplied(value); setFiltersOpen(false); }} />}
    </>}
  </Page></div>;
}

export function CatalogGameList({ items, searching = false, open, offset = 0, limit = items.length }: { items: GameListItem[]; searching?: boolean; open: (id: number) => void; offset?: number; limit?: number }) {
  const availableIds = new Set(items.map(game => game.bggId));
  return <div className="catalog-grid">{catalogGroups(items).slice(offset, offset + limit).map(game => <div className={`catalog-game-group${game.expansions?.length ? " has-expansions" : ""}`} key={game.bggId}>
    <CatalogCard game={game} open={() => open(game.bggId)} />
    {Boolean(game.expansions?.length) && <details className="collection-expansions" open={searching ? true : undefined}>
      <summary><span><strong>Дополнения</strong><small>В коллекции</small></span><Badge tone="accent">{game.expansions!.length}</Badge></summary>
      <ul className="catalog-expansion-list">{game.expansions!.map(exp => <li key={exp.bggId}>{availableIds.has(exp.bggId)
        ? <button className="catalog-expansion-option" onClick={() => open(exp.bggId)}><span aria-hidden>↳</span><span>{exp.name} <ComplexityBadge info={exp.complexityInfo} /></span><small>Открыть</small></button>
        : <div className="catalog-expansion-option static"><span aria-hidden>↳</span><span>{exp.name} <ComplexityBadge info={exp.complexityInfo} /></span><small>Дополнение</small></div>}</li>)}</ul>
    </details>}
  </div>)}</div>;
}

function CatalogCard({ game, open }: { game: GameListItem; open: () => void }) {
  return <GameCard game={game} context="catalog" open={open}>
    {(game.availabilitySummary || Boolean(game.scheduledGatherings)) && <span className="catalog-card-availability">
      {game.availabilitySummary && <span className={game.needsProviderCoordination ? "availability warning" : game.isDefinitelyAvailable ? "availability success" : "availability"}>{game.availabilitySummary}</span>}
      {Boolean(game.scheduledGatherings) && <span className="catalog-activity">{plural(game.scheduledGatherings!, "сбор", "сбора", "сборов")} впереди</span>}
    </span>}
  </GameCard>;
}
