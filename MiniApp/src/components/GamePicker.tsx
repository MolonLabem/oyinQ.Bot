import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../api/client";
import type { BggDetails, BggSearchResult, ClubGame } from "../api/types";
import { Cover, Notice } from "./Ui";
import { normalizeGameSearch, rankGames } from "./gameSearch";

export { normalizeGameSearch, searchGames } from "./gameSearch";

type GameSource = "catalog" | "bgg";

export function GamePicker({
  catalog = [], catalogLoading = false, catalogError, bggAvailable, selected, onSelect, onClear,
  label = "Найдите игру", hint = "Введите название, BGG ID или ссылку"
}: {
  catalog?: ClubGame[];
  catalogLoading?: boolean;
  catalogError?: string;
  bggAvailable: boolean;
  selected?: ClubGame;
  onSelect: (game: ClubGame, source: GameSource) => void;
  onClear?: () => void;
  label?: string;
  hint?: string;
}) {
  const [input, setInput] = useState("");
  const [results, setResults] = useState<BggSearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [loadingGame, setLoadingGame] = useState(false);
  const [open, setOpen] = useState(false);
  const [remoteLimit, setRemoteLimit] = useState(9);
  const [error, setError] = useState<string>();
  const requestVersion = useRef(0);
  const container = useRef<HTMLDivElement>(null);
  const normalized = normalizeGameSearch(input);
  const catalogMatches = useMemo(() => rankGames(catalog, normalized).slice(0, 8), [catalog, normalized]);
  const remoteResults = results.filter(result => !catalogMatches.some(game => game.bggId === result.bggId));
  const isReference = looksLikeBggReference(input);
  const selectionCurrent = Boolean(selected && input.trim() === selected.name);

  useEffect(() => {
    const close = (event: PointerEvent) => {
      if (!container.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", close);
    return () => document.removeEventListener("pointerdown", close);
  }, []);

  useEffect(() => {
    if (!bggAvailable || isReference || normalized.length < 2 || selectionCurrent) {
      setResults([]);
      setSearching(false);
      return;
    }
    const version = ++requestVersion.current;
    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      setSearching(true);
      setError(undefined);
      try {
        const value = await api<BggSearchResult[]>(`/bgg/search?query=${encodeURIComponent(input.trim())}`, { signal: controller.signal });
        if (version === requestVersion.current) setResults(value);
      } catch (reason) {
        if (!controller.signal.aborted && version === requestVersion.current)
          setError(reason instanceof Error ? reason.message : String(reason));
      } finally {
        if (version === requestVersion.current) setSearching(false);
      }
    }, 550);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [bggAvailable, input, isReference, normalized, selectionCurrent]);

  function changeInput(value: string) {
    setInput(value);
    setRemoteLimit(9);
    setOpen(true);
    setError(undefined);
    if (selected && value.trim() !== selected.name) onClear?.();
  }

  function chooseCatalog(game: ClubGame) {
    setInput(game.name);
    setResults([]);
    setOpen(false);
    setError(undefined);
    onSelect(game, "catalog");
  }

  async function chooseBgg(inputOrId: string) {
    setLoadingGame(true);
    setError(undefined);
    try {
      const details = await api<BggDetails>(`/bgg/game?input=${encodeURIComponent(inputOrId)}`);
      const game = { ...details.game, expansions: details.expansions };
      setInput(game.name);
      setResults([]);
      setOpen(false);
      onSelect(game, "bgg");
    } catch (reason) {
      setOpen(true);
      setError(reason instanceof Error ? reason.message : String(reason));
    } finally {
      setLoadingGame(false);
    }
  }

  async function searchNow() {
    const value = input.trim();
    if (!value) return;
    if (isReference) { await chooseBgg(value); return; }
    const exactLocal = catalogMatches.find(game => normalizeGameSearch(game.name) === normalized
      || (game.originalName && normalizeGameSearch(game.originalName) === normalized));
    if (exactLocal) { chooseCatalog(exactLocal); return; }
    if (!bggAvailable) {
      setError(catalog.length ? "Такой игры нет в доступной коллекции, а BGG сейчас недоступен." : "BGG сейчас недоступен.");
      return;
    }
    requestVersion.current++;
    setSearching(true);
    setError(undefined);
    try {
      const valueResults = await api<BggSearchResult[]>(`/bgg/search?query=${encodeURIComponent(value)}`);
      setResults(valueResults);
      setOpen(true);
      if (!valueResults.length && !catalogMatches.length)
        setError("BGG не нашёл совпадений. Проверьте название или вставьте ссылку на игру.");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : String(reason));
    } finally {
      setSearching(false);
    }
  }

  const showResults = open && normalized.length > 0 && (catalogMatches.length > 0 || remoteResults.length > 0 || searching);
  return <div className="game-picker" ref={container}>
    <label className="field">
      <span>{label}</span>
      <div className="search-box">
        <input type="search" role="combobox" aria-expanded={showResults} aria-controls="game-picker-results"
          autoComplete="off" value={input} onFocus={() => setOpen(true)} onChange={event => changeInput(event.target.value)}
          onKeyDown={event => { if (event.key === "Enter") { event.preventDefault(); void searchNow(); } }}
          placeholder="Например, Nemesis" />
        <button type="button" className="primary" disabled={!input.trim() || loadingGame || searching || selectionCurrent}
          onClick={() => void searchNow()}>{selectionCurrent ? "Выбрано" : loadingGame ? "Открываем…" : searching ? "Ищем…" : "Найти"}</button>
      </div>
      <small>{hint}</small>
    </label>
    {showResults && <div id="game-picker-results" className="game-picker-results" role="listbox"
      onPointerDown={event => event.preventDefault()}>
      {catalogMatches.length > 0 && <section><strong className="result-group-title">В коллекции</strong>
        {catalogMatches.map(game => <button type="button" role="option" aria-selected={selected?.bggId === game.bggId}
          className="game-search-result" key={`catalog-${game.bggId}`} onClick={() => chooseCatalog(game)}>
          <Cover src={game.thumbnailImageUrl} name={game.name} /><span><strong>{game.name}</strong><GameMeta game={game} compact /></span><span aria-hidden>›</span>
        </button>)}</section>}
      {bggAvailable && (remoteResults.length > 0 || searching) && <section><strong className="result-group-title">BoardGameGeek</strong>
        {remoteResults.slice(0, remoteLimit).map(game => <button type="button" role="option" aria-selected="false" className="game-search-result"
          key={`bgg-${game.bggId}`} onClick={() => void chooseBgg(String(game.bggId))}>
          <span className="result-icon" aria-hidden>BGG</span><span><strong>{game.name}</strong><small>{game.yearPublished ? `${game.yearPublished} год` : "Год не указан"}</small></span><span aria-hidden>›</span>
        </button>)}
        {remoteResults.length > remoteLimit && <button type="button" className="ghost" onClick={() => setRemoteLimit(limit => limit + 9)}>Показать ещё ({remoteResults.length - remoteLimit})</button>}
        {searching && <p className="picker-status">Ищем в BGG…</p>}
      </section>}
    </div>}
    {catalogLoading && <p className="picker-status">Загружаем коллекцию…</p>}
    {catalogError && <Notice kind="warning">Коллекцию загрузить не удалось: {catalogError}</Notice>}
    {error && <Notice kind="danger">{error}</Notice>}
  </div>;
}

export function GameMeta({ game, compact = false }: { game: ClubGame; compact?: boolean }) {
  const players = game.minPlayers && game.maxPlayers ? `${game.minPlayers}–${game.maxPlayers} игроков` : undefined;
  const tags = (game.typeNames?.length ? game.typeNames : [game.typeName]).filter((value): value is string => Boolean(value));
  if (!players && !tags.length) return compact ? <small>Метаданные появятся после выбора</small> : null;
  return <span className={`game-meta ${compact ? "compact" : ""}`}>
    {players && <small>{players}{game.bestPlayers ? ` · лучше: ${game.bestPlayers}` : ""}</small>}
    {tags.length > 0 && <span className="tag-list">{tags.map(tag => <span className="tag" key={tag}>{tag}</span>)}</span>}
  </span>;
}

function looksLikeBggReference(value: string) {
  return /^\d+$/.test(value.trim()) || /boardgamegeek\.com\/boardgame\//i.test(value);
}
