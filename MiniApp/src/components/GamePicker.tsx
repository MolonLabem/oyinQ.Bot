import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "../api/client";
import type { BggDetails, BggSearchResult, ClubGame } from "../api/types";
import { Cover, Notice } from "./Ui";

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
    const exactLocal = catalogMatches.find(game => normalizeGameSearch(game.name) === normalized);
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
        {remoteResults.map(game => <button type="button" role="option" aria-selected="false" className="game-search-result"
          key={`bgg-${game.bggId}`} onClick={() => void chooseBgg(String(game.bggId))}>
          <span className="result-icon" aria-hidden>BGG</span><span><strong>{game.name}</strong><small>{game.yearPublished ? `${game.yearPublished} год` : "Год не указан"}</small></span><span aria-hidden>›</span>
        </button>)}
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
  const tags = [...(game.types ?? []), ...(game.categories ?? [])];
  if (!players && !tags.length) return compact ? <small>Метаданные появятся после выбора</small> : null;
  return <span className={`game-meta ${compact ? "compact" : ""}`}>
    {players && <small>{players}{game.bestPlayers ? ` · лучше: ${game.bestPlayers}` : ""}</small>}
    {tags.length > 0 && <span className="tag-list">{tags.map(tag => <span className="tag" key={tag}>{tag}</span>)}</span>}
  </span>;
}

export function gameTagDescription(game: ClubGame): string {
  const lines: string[] = [];
  if (game.types?.length) lines.push(`Тип: ${game.types.join(", ")}`);
  if (game.categories?.length) lines.push(`Категории: ${game.categories.join(", ")}`);
  return lines.join("\n");
}

export function normalizeGameSearch(value: string) {
  return value.trim().toLocaleLowerCase("ru-RU").normalize("NFKD").replace(/[\u0300-\u036f]/g, "");
}

export function searchGames<T extends ClubGame>(games: T[], query: string): T[] {
  const normalized = normalizeGameSearch(query);
  return normalized ? rankGames(games, normalized) : games;
}

function rankGames<T extends ClubGame>(games: T[], query: string): T[] {
  if (!query) return [];
  return games.map(game => ({ game, score: matchScore(normalizeGameSearch(game.name), query) }))
    .filter(value => value.score < Number.POSITIVE_INFINITY)
    .sort((left, right) => left.score - right.score || left.game.name.localeCompare(right.game.name))
    .map(value => value.game);
}

function matchScore(candidate: string, query: string) {
  if (candidate === query) return 0;
  if (candidate.startsWith(query)) return 1;
  const index = candidate.indexOf(query);
  if (index >= 0) return 2 + index / 100;
  const words = candidate.split(/[^\p{L}\p{N}]+/u).filter(Boolean);
  const distance = Math.min(editDistance(candidate, query), ...words.map(word => editDistance(word, query)));
  const allowed = query.length >= 8 ? 2 : query.length >= 5 ? 1 : 0;
  return distance <= allowed ? 10 + distance : Number.POSITIVE_INFINITY;
}

function editDistance(left: string, right: string) {
  const row = Array.from({ length: right.length + 1 }, (_, index) => index);
  for (let i = 1; i <= left.length; i++) {
    let previous = row[0]; row[0] = i;
    for (let j = 1; j <= right.length; j++) {
      const old = row[j];
      row[j] = Math.min(row[j] + 1, row[j - 1] + 1, previous + (left[i - 1] === right[j - 1] ? 0 : 1));
      previous = old;
    }
  }
  return row[right.length];
}

function looksLikeBggReference(value: string) {
  return /^\d+$/.test(value.trim()) || /boardgamegeek\.com\/boardgame\//i.test(value);
}
