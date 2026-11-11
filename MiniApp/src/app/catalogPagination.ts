import type { GameListItem } from "../api/types";

// Pagination counts visible cards, keeping expansions with their base game.
export function catalogGroups(items: GameListItem[]) {
  const nestedIds = new Set(items.flatMap(game => (game.expansions ?? []).filter(exp => exp.bggId !== game.bggId).map(exp => exp.bggId)));
  return items.filter(game => !nestedIds.has(game.bggId));
}
