import { describe, expect, it } from "vitest";
import type { ClubGame } from "../api/types";
import { searchGames } from "./gameSearch";

describe("searchGames", () => {
  const game = {
    bggId: 167791,
    name: "Покорение Марса",
    originalName: "Terraforming Mars",
    type: "Strategy",
    expansions: [],
  } as ClubGame;

  it("finds a localized game by both display and original BGG name", () => {
    expect(searchGames([game], "Покорение")).toEqual([game]);
    expect(searchGames([game], "Terraforming")).toEqual([game]);
  });
});
