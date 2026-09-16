import { describe, expect, it } from "vitest";
import { activeGroups, catalogParams, emptyFilters, filterChips, filterStorageKey, normalizeFilters, showGames } from "./catalogFilters";
const options = { categories: [{ bggId: 2, name: "Экономика" }], types: [{ key: "Strategy" as const, value: "Стратегия" }], providers: [{ participantId: 4, displayName: "Анна" }] };
describe("contextual catalog filters", () => {
  it("preserves complexity, mechanics, duration and Camp date through the shared panel query", () => {
    const available = { ...options, complexities: [{ level: "MediumHeavy", displayName: "Сложная", cssClass: "complexity-hard" }], mechanics: [{ bggId: 9, name: "Аукцион" }] };
    const f = normalizeFilters({ complexities: ["MediumHeavy", "missing"], mechanics: [9, 99], maxDurationMinutes: 90, attendanceDate: "2026-09-18" }, "Camp", available);
    const query = new URLSearchParams(catalogParams("camp", "Camp", f, "", "name"));
    expect(query.get("complexities")).toBe("MediumHeavy");
    expect(query.get("mechanics")).toBe("9");
    expect(query.get("maxDurationMinutes")).toBe("90");
    expect(query.get("attendanceDate")).toBe("2026-09-18");
    expect(activeGroups(f)).toBe(4);
    expect(filterChips(f, available).map(x => x.label)).toEqual(["2026-09-18", "До 90 мин", "Сложная", "Аукцион"]);
    expect(normalizeFilters(f, "Club", available).attendanceDate).toBeUndefined();
  });
  it("removes obsolete and camp-only constraints from Club, including requests and counts", () => {
    const f = normalizeFilters({ availability: "possible", ownership: "participants", providers: [4], categories: [2,99], types: ["Strategy", "bad"], players: -1, planning: "old" }, "Club", options);
    expect(f).toEqual({ ...emptyFilters(), categories: [2], types: ["Strategy"] });
    expect(activeGroups(f)).toBe(2);
    expect(catalogParams("club", "Club", f, "", "popular")).not.toMatch(/availability|providers|ownership/);
  });
  it("retains Camp selections, deduplicates and drops unavailable participants", () => {
    const f = normalizeFilters({ ownership: "participants", availability: "confirmed", providers: [4,4,5], categories: [2,2] }, "Camp", options);
    expect(f.providers).toEqual([4]); expect(activeGroups(f)).toBe(4);
    expect(filterChips(f, options).map(x => x.label)).toContain("Анна");
    expect(normalizeFilters({ ...f, ownership: "mine" }, "Camp", options).providers).toEqual([]);
    expect(filterStorageKey("a", "Camp")).not.toBe(filterStorageKey("b", "Camp"));
  });
  it("uses Russian accusative counts and counts groups rather than values", () => {
    expect([0,1,2,5,11,21,22,101,112].map(showGames)).toEqual(["Показать 0 игр","Показать 1 игру","Показать 2 игры","Показать 5 игр","Показать 11 игр","Показать 21 игру","Показать 22 игры","Показать 101 игру","Показать 112 игр"]);
    expect(activeGroups({ ...emptyFilters(), categories: [1,2,3], types: ["Strategy", "Family"] })).toBe(2);
    expect(activeGroups(normalizeFilters(null, "Club"))).toBe(0);
  });
});
