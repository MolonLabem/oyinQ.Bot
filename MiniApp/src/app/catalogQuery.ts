import type { GameType } from "../api/types";

export type PlayerCountMode = "supported" | "best";

export type CatalogFilterState = {
  communityKey: string;
  search: string;
  players?: number;
  playerCountMode?: PlayerCountMode;
  fromYear?: number;
  toYear?: number;
  types: GameType[];
  categories: number[];
  sort: string;
  complexities?: string[];
  mechanics?: number[];
  maxDurationMinutes?: number;
  attendanceDate?: string;
};

export function buildCatalogQuery(state: CatalogFilterState) {
  const params = new URLSearchParams({ community: state.communityKey, sort: state.sort });
  if (state.search.trim()) params.set("search", state.search.trim());
  if (state.players) params.set("players", String(state.players));
  if (state.players && state.playerCountMode === "best") params.set("playerCountMode", "best");
  if (state.fromYear !== undefined) params.set("fromYear", String(state.fromYear));
  if (state.toYear !== undefined) params.set("toYear", String(state.toYear));
  if (state.types.length) params.set("types", state.types.join(","));
  if (state.categories.length) params.set("categories", state.categories.join(","));
  if (state.complexities?.length) params.set("complexities", state.complexities.join(","));
  if (state.mechanics?.length) params.set("mechanics", state.mechanics.join(","));
  if (state.maxDurationMinutes) params.set("maxDurationMinutes", String(state.maxDurationMinutes));
  if (state.attendanceDate) params.set("attendanceDate", state.attendanceDate);
  return params.toString();
}

export function toggleValue<T>(values: T[], value: T) {
  return values.includes(value) ? values.filter(item => item !== value) : [...values, value];
}
