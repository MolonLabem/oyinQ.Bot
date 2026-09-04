import type { BggBaseGameSearchResult, BggDetails, ClubGame } from "../api/types";

export type GameSource = "catalog" | "bgg";

export type GameSearchCandidate = BggBaseGameSearchResult & {
  localGame?: ClubGame;
};

export function mergeGameSearchCandidates(
  localGames: ClubGame[],
  remoteResults: BggBaseGameSearchResult[]
): GameSearchCandidate[] {
  const merged = new Map<number, GameSearchCandidate>();

  for (const game of localGames) {
    if (merged.has(game.bggId)) continue;
    merged.set(game.bggId, {
      bggId: game.bggId,
      name: game.name,
      originalName: game.originalName,
      yearPublished: game.yearPublished,
      localGame: game,
    });
  }

  for (const result of remoteResults) {
    const existing = merged.get(result.bggId);
    if (!existing) {
      merged.set(result.bggId, result);
      continue;
    }

    // Saved names are often localized. Keep that preferred name and fill only
    // missing alias/year metadata from the canonical search candidate.
    merged.set(result.bggId, {
      ...result,
      ...existing,
      originalName: existing.originalName ?? result.originalName,
      yearPublished: existing.yearPublished ?? result.yearPublished,
    });
  }

  return [...merged.values()];
}

export async function resolveGameSelection(
  candidate: GameSearchCandidate,
  bggAvailable: boolean,
  loadDetails: (bggId: number) => Promise<BggDetails>
): Promise<{ game: ClubGame; source: GameSource; fallbackWarning?: string }> {
  const source: GameSource = candidate.localGame ? "catalog" : "bgg";
  if (!bggAvailable) {
    if (!candidate.localGame) throw new Error("BGG сейчас недоступен.");
    return { game: candidate.localGame, source };
  }

  try {
    const details = await loadDetails(candidate.bggId);
    return {
      game: { ...details.game, expansions: uniqueByBggId(details.expansions) },
      source,
    };
  } catch (reason) {
    if (!candidate.localGame) throw reason;
    return {
      game: candidate.localGame,
      source,
      fallbackWarning: "BGG не ответил. Используем сохранённые данные игры и дополнений.",
    };
  }
}

export function dismissGamePickerSearch(input: Pick<HTMLInputElement, "blur"> | null) {
  input?.blur();
}

export function uniqueByBggId<T extends { bggId: number }>(values: T[]) {
  return [...new Map(values.map(value => [value.bggId, value])).values()];
}
