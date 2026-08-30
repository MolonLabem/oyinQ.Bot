import type { GameType } from "../api/types";

export type CatalogFilterState = {
  communityKey: string;
  search: string;
  players?: number;
  types: GameType[];
  categories: number[];
  sort: string;
};

export function buildCatalogQuery(state: CatalogFilterState) {
  const params = new URLSearchParams({ community: state.communityKey, sort: state.sort });
  if (state.search.trim()) params.set("search", state.search.trim());
  if (state.players) params.set("players", String(state.players));
  if (state.types.length) params.set("types", state.types.join(","));
  if (state.categories.length) params.set("categories", state.categories.join(","));
  return params.toString();
}

export function toggleValue<T>(values: T[], value: T) {
  return values.includes(value) ? values.filter(item => item !== value) : [...values, value];
}
