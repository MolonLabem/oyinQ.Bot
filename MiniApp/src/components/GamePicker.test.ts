import { describe, expect, it } from "vitest";
import type { BggDetails, ClubGame } from "../api/types";
import { dismissGamePickerSearch, mergeGameSearchCandidates, resolveGameSelection } from "./gamePickerModel";
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

  it("merges local, localized and remote aliases into one BGG identity", () => {
    const results = mergeGameSearchCandidates([game], [
      { bggId: 167791, name: "Terraforming Mars", yearPublished: 2016 },
      { bggId: 167791, name: "Покорение Марса", originalName: "Terraforming Mars" },
    ]);

    expect(results).toHaveLength(1);
    expect(results[0]).toMatchObject({
      bggId: 167791,
      name: "Покорение Марса",
      originalName: "Terraforming Mars",
      localGame: game,
    });
  });

  it("loads canonical details when a local game is selected", async () => {
    const canonical = {
      game: { ...game, name: "Terraforming Mars", expansions: [] },
      expansions: [{ bggId: 2001, name: "Prelude" }],
    } satisfies BggDetails;
    let detailRequests = 0;

    const result = await resolveGameSelection(
      { bggId: game.bggId, name: game.name, localGame: game },
      true,
      async () => { detailRequests++; return canonical; }
    );

    expect(detailRequests).toBe(1);
    expect(result.source).toBe("catalog");
    expect(result.game.expansions).toEqual([{ bggId: 2001, name: "Prelude" }]);
  });

  it("показывает дополнения для игры только из BGG без локальной коллекции", async () => {
    let requests = 0;
    const result = await resolveGameSelection({ bggId: 42, name: "Внешняя игра" }, true, async () => {
      requests++;
      return { game: { ...game, bggId: 42 }, expansions: [{ bggId: 99, name: "Дополнение" }] };
    });
    expect(result.source).toBe("bgg"); expect(result.game.expansions).toEqual([{ bggId: 99, name: "Дополнение" }]); expect(requests).toBe(1);
  });

  it("keeps saved expansion data when BGG is unavailable", async () => {
    const saved = { ...game, expansions: [{ bggId: 3001, name: "Сохранённое дополнение" }] };
    let detailRequests = 0;

    const result = await resolveGameSelection(
      { bggId: saved.bggId, name: saved.name, localGame: saved },
      false,
      async () => { detailRequests++; throw new Error("must not run"); }
    );

    expect(detailRequests).toBe(0);
    expect(result.game).toBe(saved);
    expect(result.game.expansions).toHaveLength(1);
  });

  it("fails remote-only discovery gracefully when BGG is unavailable", async () => {
    await expect(resolveGameSelection(
      { bggId: 999, name: "Remote game" },
      false,
      async () => { throw new Error("must not run"); }
    )).rejects.toThrow("BGG сейчас недоступен.");
  });

  it("dismisses the software keyboard after selection", () => {
    let blurred = false;
    dismissGamePickerSearch({ blur: () => { blurred = true; } });
    expect(blurred).toBe(true);
  });
});
