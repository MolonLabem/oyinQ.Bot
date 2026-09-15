import type { BggBaseGameSearchResult, BggDetails, ClubGame, Expansion } from "../api/types";

export type ExpansionLookup = { status: "complete" | "incomplete" | "failed" };

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
): Promise<{ game: ClubGame; source: GameSource; fallbackWarning?: string; expansionLookup: ExpansionLookup; selectedExpansionIds?: number[]; baseGames?: BggDetails["baseGames"] }> {
  const source: GameSource = candidate.localGame ? "catalog" : "bgg";
  if (!bggAvailable) {
    if (!candidate.localGame) throw new Error("BGG сейчас недоступен.");
    return { game: candidate.localGame, source, expansionLookup: { status: "failed" } };
  }

  try {
    const details = await loadDetails(candidate.bggId);
    return {
      game: { ...details.game, expansions: mergeExpansions(
        details.game.bggId === candidate.bggId ? candidate.localGame?.expansions ?? [] : [], details.expansions) },
      source: details.game.bggId === candidate.bggId ? source : "bgg",
      expansionLookup: { status: details.expansionLookupIncomplete ? "incomplete" : "complete" },
      selectedExpansionIds: details.selectedExpansionIds, baseGames: details.baseGames,
    };
  } catch (reason) {
    if (!candidate.localGame) throw reason;
    return {
      game: candidate.localGame,
      source,
      expansionLookup: { status: "failed" },
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

// Provider omissions must not erase saved metadata or attached expansions.
export function mergeExpansions(saved: Expansion[], additions: Expansion[]): Expansion[] {
  const merged = new Map<number, Expansion>();
  for (const item of [...saved, ...additions]) {
    const previous = merged.get(item.bggId);
    merged.set(item.bggId, previous ? { ...item,
      originalName: item.originalName ?? previous.originalName,
      minPlayers: item.minPlayers ?? previous.minPlayers,
      maxPlayers: item.maxPlayers ?? previous.maxPlayers,
      thumbnailImageUrl: item.thumbnailImageUrl ?? previous.thumbnailImageUrl,
      imageUrl: item.imageUrl ?? previous.imageUrl,
      complexityInfo: item.complexityInfo ?? previous.complexityInfo,
    } : item);
  }
  return [...merged.values()];
}
