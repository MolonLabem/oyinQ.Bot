import { wishlistCopy } from "./productCopy";
import type { CatalogResponse, CommunityMode, GameType } from "../api/types";
import { plural } from "./format";
import { buildCatalogQuery, type PlayerCountMode } from "./catalogQuery";

export type Filters = { players?: number; playerCountMode: PlayerCountMode; fromYear?: number; toYear?: number; types: GameType[]; categories: number[]; ownership: string; availability: string; planning: string; providers: number[]; complexities: string[]; mechanics: number[]; maxDurationMinutes?: number; attendanceDate?: string };
export type FilterOptions = CatalogResponse["filters"];
export const emptyFilters = (): Filters => ({ playerCountMode: "supported", types: [], categories: [], ownership: "", availability: "", planning: "", providers: [], complexities: [], mechanics: [] });
export const minPublicationYear = 1;
export const maxPublicationYear = 9999;
const validYear = (year: unknown): year is number => typeof year === "number" && Number.isInteger(year) && year >= minPublicationYear && year <= maxPublicationYear;
export function yearFilterError(f: Pick<Filters, "fromYear" | "toYear">): string | undefined {
  if ([f.fromYear, f.toYear].some(year => year !== undefined && !validYear(year))) return `Укажите целый год от ${minPublicationYear} до ${maxPublicationYear}.`;
  if (f.fromYear !== undefined && f.toYear !== undefined && f.fromYear > f.toYear) return "Год «От» не должен быть позже года «До».";
}
export function yearFilterLabel(f: Pick<Filters, "fromYear" | "toYear">) {
  if (f.fromYear !== undefined && f.toYear !== undefined) return `${f.fromYear}–${f.toYear}`;
  if (f.fromYear !== undefined) return `От ${f.fromYear}`;
  if (f.toYear !== undefined) return `До ${f.toYear}`;
  return "Любой";
}
export const ownershipLabels: Record<string, string> = { club: "Коллекция клуба", mine: "Есть у меня", wishes: wishlistCopy.mine, participants: "Игры участников" };
export const availabilityLabels: Record<string, string> = { confirmed: "Точно привезут", possible: "Нужно договориться" };
export const planningLabels: Record<string, string> = { planned: "Уже запланированы", unplanned: "Ещё не запланированы" };
const gameTypes: GameType[] = ["Strategy", "Family", "Party", "Thematic", "Abstract", "War", "Children", "Customizable", "Other"];
const ids = (value: unknown): number[] => Array.isArray(value) ? [...new Set(value.filter((id): id is number => typeof id === "number" && Number.isSafeInteger(id) && id > 0))] : [];
export function normalizeFilters(value: unknown, mode: CommunityMode, options?: FilterOptions): Filters {
  const f = (value && typeof value === "object" ? value : {}) as Partial<Filters>;
  const ownership = typeof f.ownership === "string" && Object.hasOwn(ownershipLabels, f.ownership) && (mode === "Camp" || f.ownership !== "participants") ? f.ownership : "";
  return {
    players: typeof f.players === "number" && Number.isSafeInteger(f.players) && f.players > 0 ? f.players : undefined,
    playerCountMode: f.playerCountMode === "best" ? "best" : "supported",
    fromYear: validYear(f.fromYear) ? f.fromYear : undefined,
    toYear: validYear(f.toYear) ? f.toYear : undefined,
    types: Array.isArray(f.types) ? [...new Set(f.types.filter(x => gameTypes.includes(x) && (!options || options.types.some(o => o.key === x))))] : [],
    categories: ids(f.categories).filter(x => !options || options.categories.some(o => o.bggId === x)),
    complexities: Array.isArray(f.complexities) ? [...new Set(f.complexities.filter(x => typeof x === "string" && (!options || options.complexities?.some(o => o.level === x))))] : [],
    mechanics: ids(f.mechanics).filter(x => !options || options.mechanics?.some(o => o.bggId === x)),
    maxDurationMinutes: typeof f.maxDurationMinutes === "number" && Number.isSafeInteger(f.maxDurationMinutes) && f.maxDurationMinutes > 0 && f.maxDurationMinutes <= 10080 ? f.maxDurationMinutes : undefined,
    attendanceDate: mode === "Camp" && typeof f.attendanceDate === "string" && /^\d{4}-\d{2}-\d{2}$/.test(f.attendanceDate) ? f.attendanceDate : undefined,
    ownership, availability: mode === "Camp" && typeof f.availability === "string" && Object.hasOwn(availabilityLabels, f.availability) ? f.availability : "",
    planning: typeof f.planning === "string" && Object.hasOwn(planningLabels, f.planning) ? f.planning : "",
    providers: mode === "Camp" && ownership === "participants" ? ids(f.providers).filter(x => !options || options.providers?.some(o => o.participantId === x)) : []
  };
}
export function activeGroups(f: Filters) {
  return [Boolean(f.players), f.fromYear !== undefined || f.toYear !== undefined, f.types.length > 0, f.categories.length > 0, Boolean(f.ownership), Boolean(f.availability), Boolean(f.planning), f.providers.length > 0, f.complexities.length > 0, f.mechanics.length > 0, Boolean(f.maxDurationMinutes), Boolean(f.attendanceDate)].filter(Boolean).length;
}
export function catalogParams(key: string, mode: CommunityMode, f: Filters, search: string, sort: string) {
  const normalized = normalizeFilters(f, mode);
  const params = new URLSearchParams(buildCatalogQuery({ communityKey: key, search, sort, ...normalized }));
  for (const name of ["ownership", "availability", "planning"] as const) if (normalized[name]) params.set(name, normalized[name]);
  if (normalized.providers.length) params.set("providers", normalized.providers.join(","));
  return params.toString();
}
export function gameCount(n: number) { return plural(n, "игра", "игры", "игр"); }
export function showGames(n: number) { return `Показать ${gameCount(n).replace(/игра$/, "игру")}`; }
export function filterChips(f: Filters, options?: FilterOptions): { key: string; label: string; remove: Filters }[] {
  return [
    ...(f.attendanceDate ? [{ key: "day", label: f.attendanceDate, remove: { ...f, attendanceDate: undefined } }] : []),
    ...(f.maxDurationMinutes ? [{ key: "duration", label: `До ${f.maxDurationMinutes} мин`, remove: { ...f, maxDurationMinutes: undefined } }] : []),
    ...(f.fromYear !== undefined || f.toYear !== undefined ? [{ key: "year", label: `Год выпуска: ${yearFilterLabel(f)}`, remove: { ...f, fromYear: undefined, toYear: undefined } }] : []),
    ...f.complexities.map(x => ({ key: `complexity-${x}`, label: options?.complexities?.find(o => o.level === x)?.displayName ?? x, remove: { ...f, complexities: f.complexities.filter(v => v !== x) } })),
    ...f.mechanics.map(x => ({ key: `mechanic-${x}`, label: options?.mechanics?.find(o => o.bggId === x)?.name ?? "Механика", remove: { ...f, mechanics: f.mechanics.filter(v => v !== x) } })),
    ...(f.players ? [{ key: "players", label: `${f.playerCountMode === "best" ? "Лучше всего" : "Игроков"}: ${f.players}`, remove: { ...f, players: undefined, playerCountMode: "supported" as const } }] : []),
    ...([ ["ownership", ownershipLabels], ["availability", availabilityLabels], ["planning", planningLabels] ] as const).flatMap(([key, labels]) => f[key] ? [{ key, label: labels[f[key]], remove: { ...f, [key]: "", ...(key === "ownership" ? { providers: [] } : {}) } }] : []),
    ...f.types.map(x => ({ key: `type-${x}`, label: options?.types.find(o => o.key === x)?.value ?? x, remove: { ...f, types: f.types.filter(v => v !== x) } })),
    ...f.categories.map(x => ({ key: `category-${x}`, label: options?.categories.find(o => o.bggId === x)?.name ?? "Категория", remove: { ...f, categories: f.categories.filter(v => v !== x) } })),
    ...f.providers.map(x => ({ key: `provider-${x}`, label: options?.providers?.find(o => o.participantId === x)?.displayName ?? "Участник", remove: { ...f, providers: f.providers.filter(v => v !== x) } }))
  ];
}
export const filterStorageKey = (key: string, mode: CommunityMode) => `oyinq-catalog-filters-v1:${mode}:${key}`;
export function restoreFilters(key: string, mode: CommunityMode) {
  try { return normalizeFilters(JSON.parse(sessionStorage.getItem(filterStorageKey(key, mode)) ?? "null"), mode); }
  catch { return emptyFilters(); }
}
