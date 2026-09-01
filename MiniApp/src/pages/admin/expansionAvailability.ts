import type { ClubGame, Expansion } from "../../api/types";

export function availableExpansions(expansions: readonly Expansion[] | null | undefined): readonly Expansion[] {
  return expansions ?? [];
}

export function hasAvailableExpansions(game: Pick<ClubGame, "expansions">): boolean {
  return availableExpansions(game.expansions).length > 0;
}

export function toggleExpansionList(currentBggId: number | undefined, game: Pick<ClubGame, "bggId" | "expansions">): number | undefined {
  if (!hasAvailableExpansions(game)) return undefined;
  return currentBggId === game.bggId ? undefined : game.bggId;
}
