import { describe, expect, it } from "vitest";
import { activeGroups, catalogParams, emptyFilters, filterChips, filterStorageKey, normalizeFilters, showGames, yearFilterError } from "./catalogFilters";
const options = { categories: [{ bggId: 2, name: "Экономика" }], types: [{ key: "Strategy" as const, value: "Стратегия" }], providers: [{ participantId: 4, displayName: "Анна" }] };
describe("contextual catalog filters", () => {
  it.each([
    [2015, undefined, "От 2015"],
    [undefined, 2015, "До 2015"],
    [2015, 2020, "2015–2020"],
    [2015, 2015, "2015–2015"]
  ])("serializes open and inclusive year bounds %s / %s as one group", (fromYear, toYear, label) => {
    const f = normalizeFilters({ fromYear, toYear }, "Club");
    expect(yearFilterError(f)).toBeUndefined();
    const params = new URLSearchParams(catalogParams("club", "Club", f, "", "name"));
    expect(params.get("fromYear")).toBe(fromYear === undefined ? null : String(fromYear));
    expect(params.get("toYear")).toBe(toYear === undefined ? null : String(toYear));
    expect(activeGroups(f)).toBe(1);
    const chip = filterChips(f)[0];
    expect(chip.label).toBe(`Год выпуска: ${label}`);
    expect(activeGroups(chip.remove)).toBe(0);
    expect(catalogParams("club", "Club", chip.remove, "", "name")).not.toMatch(/fromYear|toYear/);
  });
  it("reports reversed and invalid years without swapping bounds or limiting to today's year", () => {
    const reversed = normalizeFilters({ fromYear: 2020, toYear: 2015 }, "Club");
    expect(reversed).toMatchObject({ fromYear: 2020, toYear: 2015 });
    expect(yearFilterError(reversed)).toContain("не должен быть позже");
    for (const year of [0, -1, 10000, 2015.5, NaN, Infinity]) expect(yearFilterError({ fromYear: year })).toContain("целый год");
    expect(yearFilterError({ fromYear: new Date().getFullYear() + 5 })).toBeUndefined();
    expect(yearFilterError({})).toBeUndefined();
  });
  it("uses a single players group with distinct best-mode chips and backwards compatible defaults", () => {
    const legacy = normalizeFilters({ players: 4 }, "Camp");
    expect(legacy.playerCountMode).toBe("supported");
    expect(filterChips(legacy)[0].label).toBe("Игроков: 4");
    const best = { ...legacy, playerCountMode: "best" as const, fromYear: 2015, toYear: 2020 };
    const params = new URLSearchParams(catalogParams("camp", "Camp", best, "Игра", "players"));
    expect(params.get("playerCountMode")).toBe("best");
    expect(params.get("players")).toBe("4");
    expect(activeGroups(best)).toBe(2);
    const chip = filterChips(best).find(x => x.key === "players")!;
    expect(chip.label).toBe("Лучше всего: 4");
    expect(chip.remove.playerCountMode).toBe("supported");
    expect(catalogParams("camp", "Camp", chip.remove, "", "name")).not.toMatch(/players|playerCountMode/);
    expect(catalogParams("camp", "Camp", legacy, "", "name")).not.toContain("playerCountMode");
    expect(activeGroups({ ...emptyFilters(), playerCountMode: "best" })).toBe(0);
    expect(catalogParams("camp", "Camp", emptyFilters(), "", "name")).not.toMatch(/players|playerCountMode|fromYear|toYear/);
  });
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
