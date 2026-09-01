import type { ClubGame } from "../api/types";

export function normalizeGameSearch(value: string) {
  return value.trim().toLocaleLowerCase("ru-RU").normalize("NFKD").replace(/[\u0300-\u036f]/g, "");
}

export function searchGames<T extends ClubGame>(games: T[], query: string): T[] {
  const normalized = normalizeGameSearch(query);
  return normalized ? rankGames(games, normalized) : games;
}

export function rankGames<T extends ClubGame>(games: T[], query: string): T[] {
  if (!query) return [];
  return games.map(game => ({ game, score: Math.min(
    matchScore(normalizeGameSearch(game.name), query),
    game.originalName ? matchScore(normalizeGameSearch(game.originalName), query) : Number.POSITIVE_INFINITY,
  ) }))
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
