import { describe, expect, it } from "vitest";
import type { ClubGame, Expansion } from "../../api/types";
import { availableExpansions, hasAvailableExpansions, toggleExpansionList } from "./expansionAvailability";

function game(expansions?: Expansion[] | null): Pick<ClubGame, "bggId" | "expansions"> {
  return { bggId: 42, expansions: expansions as Expansion[] };
}

describe("club expansion availability", () => {
  it("hides the action for zero expansions", () => {
    expect(hasAvailableExpansions(game([]))).toBe(false);
  });

  it("shows the action for one expansion", () => {
    expect(hasAvailableExpansions(game([{ bggId: 7, name: "One" }]))).toBe(true);
  });

  it("returns every available expansion", () => {
    const expansions = [{ bggId: 7, name: "One" }, { bggId: 8, name: "Two" }];
    expect(availableExpansions(expansions)).toEqual(expansions);
  });

  it("treats an empty expansion array as unavailable", () => {
    expect(availableExpansions([])).toEqual([]);
    expect(hasAvailableExpansions(game([]))).toBe(false);
  });

  it("treats null and missing expansion data safely", () => {
    expect(availableExpansions(null)).toEqual([]);
    expect(availableExpansions(undefined)).toEqual([]);
    expect(hasAvailableExpansions(game(null))).toBe(false);
    expect(hasAvailableExpansions(game(undefined))).toBe(false);
  });

  it("does not open an empty expansion UI", () => {
    expect(toggleExpansionList(undefined, game([]))).toBeUndefined();
    expect(toggleExpansionList(99, game(null))).toBeUndefined();
  });

  it("opens and closes a meaningful expansion list", () => {
    const withExpansion = game([{ bggId: 7, name: "One" }]);
    expect(toggleExpansionList(undefined, withExpansion)).toBe(42);
    expect(toggleExpansionList(42, withExpansion)).toBeUndefined();
  });
});
