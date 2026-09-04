import { useEffect, useId, useMemo, useRef, useState, type CSSProperties } from "react";
import { api } from "../api/client";
import type { BggBaseGameSearchResult, BggDetails, ClubGame } from "../api/types";
import { Cover, Notice } from "./Ui";
import { normalizeGameSearch, rankGames } from "./gameSearch";
import {
  dismissGamePickerSearch,
  mergeGameSearchCandidates,
  resolveGameSelection,
  uniqueByBggId,
  type GameSearchCandidate,
  type GameSource,
} from "./gamePickerModel";

export { normalizeGameSearch, searchGames } from "./gameSearch";

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
  const [results, setResults] = useState<BggBaseGameSearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [loadingGame, setLoadingGame] = useState(false);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string>();
  const [visibleViewport, setVisibleViewport] = useState<{ height: number; top: number }>();
  const requestVersion = useRef(0);
  const container = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const inputId = useId();
  const resultsId = useId();
  const normalized = normalizeGameSearch(input);
  const catalogMatches = useMemo(() => rankGames(catalog, normalized).slice(0, 25), [catalog, normalized]);
  const candidates = useMemo(() => mergeGameSearchCandidates(catalogMatches, results), [catalogMatches, results]);
  const isReference = looksLikeBggReference(input);
  const selectionCurrent = Boolean(selected && input.trim() === selected.name);
  const searchMode = open && normalized.length > 0 && !selectionCurrent;

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
        const value = await api<BggBaseGameSearchResult[]>(`/bgg/search?query=${encodeURIComponent(input.trim())}`, { signal: controller.signal });
        if (version === requestVersion.current) setResults(value);
      } catch (reason) {
        if (!controller.signal.aborted && version === requestVersion.current)
          setError(reason instanceof Error ? reason.message : String(reason));
      } finally {
        if (version === requestVersion.current) setSearching(false);
      }
    }, 400);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [bggAvailable, input, isReference, normalized, selectionCurrent]);

  useEffect(() => {
    if (!searchMode) return;
    const viewport = window.visualViewport;
    const update = () => setVisibleViewport({
      height: viewport?.height ?? window.innerHeight,
      top: viewport?.offsetTop ?? 0,
    });
    update();
    viewport?.addEventListener("resize", update);
    viewport?.addEventListener("scroll", update);
    document.body.classList.add("game-picker-open");
    return () => {
      viewport?.removeEventListener("resize", update);
      viewport?.removeEventListener("scroll", update);
      document.body.classList.remove("game-picker-open");
    };
  }, [searchMode]);

  function changeInput(value: string) {
    setInput(value);
    setOpen(true);
    setError(undefined);
    if (selected && value.trim() !== selected.name) onClear?.();
  }

  function closeSearch() {
    setOpen(false);
    dismissGamePickerSearch(inputRef.current);
  }

  async function chooseCandidate(candidate: GameSearchCandidate) {
    setLoadingGame(true);
    setError(undefined);
    closeSearch();
    try {
      const resolved = await resolveGameSelection(candidate, bggAvailable, bggId =>
        api<BggDetails>(`/bgg/game?input=${encodeURIComponent(String(bggId))}`)
      );
      setInput(resolved.game.name);
      setResults([]);
      setError(resolved.fallbackWarning);
      onSelect(resolved.game, resolved.source);
    } catch (reason) {
      setOpen(true);
      setError(reason instanceof Error ? reason.message : String(reason));
    } finally {
      setLoadingGame(false);
    }
  }

  async function chooseReference(value: string) {
    if (!bggAvailable) {
      setError("BGG сейчас недоступен. Выберите игру из сохранённой коллекции.");
      return;
    }
    setLoadingGame(true);
    setError(undefined);
    closeSearch();
    try {
      const details = await api<BggDetails>(`/bgg/game?input=${encodeURIComponent(value)}`);
      const game = { ...details.game, expansions: uniqueByBggId(details.expansions) };
      setInput(game.name);
      setResults([]);
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
    if (isReference) {
      const localId = /^\d+$/.test(value) ? Number(value) : undefined;
      const local = localId ? catalog.find(game => game.bggId === localId) : undefined;
      if (local) await chooseCandidate({ bggId: local.bggId, name: local.name, originalName: local.originalName, yearPublished: local.yearPublished, localGame: local });
      else await chooseReference(value);
      return;
    }
    const exact = candidates.find(candidate => normalizeGameSearch(candidate.name) === normalized
      || (candidate.originalName && normalizeGameSearch(candidate.originalName) === normalized));
    if (exact) {
      await chooseCandidate(exact);
      return;
    }
    if (!bggAvailable) {
      setError(catalog.length ? "Такой игры нет в доступной коллекции, а BGG сейчас недоступен." : "BGG сейчас недоступен.");
      return;
    }
    requestVersion.current++;
    setSearching(true);
    setError(undefined);
    try {
      const valueResults = await api<BggBaseGameSearchResult[]>(`/bgg/search?query=${encodeURIComponent(value)}`);
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

  const hasResults = candidates.length > 0;
  const overlayStyle = visibleViewport ? {
    "--game-picker-visible-height": `${visibleViewport.height}px`,
    "--game-picker-visible-top": `${visibleViewport.top}px`,
  } as CSSProperties : undefined;

  return <div className={`game-picker${searchMode ? " search-mode" : ""}`} ref={container} style={overlayStyle}
    role={searchMode ? "dialog" : undefined} aria-modal={searchMode || undefined} aria-label={searchMode ? "Поиск настольной игры" : undefined}>
    <div className="field">
      <div className="game-picker-heading"><label htmlFor={inputId}>{label}</label>{searchMode && <button type="button" className="ghost" onClick={closeSearch}>Закрыть</button>}</div>
      <div className="search-box">
        <input id={inputId} ref={inputRef} type="search" role="combobox" aria-expanded={searchMode} aria-controls={resultsId}
          autoComplete="off" value={input} onFocus={() => setOpen(true)} onChange={event => changeInput(event.target.value)}
          onKeyDown={event => { if (event.key === "Enter") { event.preventDefault(); void searchNow(); } }}
          placeholder="Например, Nemesis" />
        <button type="button" className="primary" disabled={!input.trim() || loadingGame || searching || selectionCurrent}
          onClick={() => void searchNow()}>{selectionCurrent ? "Выбрано" : loadingGame ? "Открываем…" : searching ? "Ищем…" : "Найти"}</button>
      </div>
      {!searchMode && <small>{hint}</small>}
    </div>
    {searchMode && <div id={resultsId} className="game-picker-results" role="listbox" aria-busy={searching}>
      {candidates.map(candidate => <button type="button" role="option" aria-selected={selected?.bggId === candidate.bggId}
        className="game-search-result" key={candidate.bggId} onClick={() => void chooseCandidate(candidate)}>
        {candidate.localGame?.thumbnailImageUrl
          ? <Cover src={candidate.localGame.thumbnailImageUrl} name={candidate.name} />
          : <span className="result-icon" aria-hidden>BGG</span>}
        <span><strong>{candidate.name}</strong><SearchResultMeta candidate={candidate} /></span><span aria-hidden>›</span>
      </button>)}
      {searching && <p className="picker-status">Ищем в BGG…</p>}
      {!searching && !hasResults && normalized.length < 2 && <p className="picker-status">Введите хотя бы два символа.</p>}
      {!searching && !hasResults && normalized.length >= 2 && !error && <p className="picker-status">Совпадений пока нет.</p>}
    </div>}
    {!searchMode && catalogLoading && <p className="picker-status">Загружаем коллекцию…</p>}
    {!searchMode && catalogError && <Notice kind="warning">Коллекцию загрузить не удалось: {catalogError}</Notice>}
    {error && <Notice kind={error.startsWith("BGG не ответил") ? "warning" : "danger"}>{error}</Notice>}
  </div>;
}

function SearchResultMeta({ candidate }: { candidate: GameSearchCandidate }) {
  const originalName = candidate.originalName?.trim();
  const differs = originalName && normalizeGameSearch(originalName) !== normalizeGameSearch(candidate.name);
  const details = [differs ? originalName : undefined, candidate.yearPublished ? String(candidate.yearPublished) : undefined]
    .filter((value): value is string => Boolean(value));
  return <small>{details.length ? details.join(" · ") : "Настольная игра"}{candidate.localGame && <span className="local-result-note"> · В вашей коллекции</span>}</small>;
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
