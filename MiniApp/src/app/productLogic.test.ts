import { describe, expect, it } from "vitest";
import { plural } from "./format";
import { buildCatalogQuery, toggleValue } from "./catalogQuery";
import { defaultImportSelection, isImportItemSelectable } from "../pages/camp/importSelection";
import type { ImportDraftItem } from "../api/types";

describe("Russian product helpers", () => {
  it("uses Russian plural forms", () => {
    expect(plural(1, "игра", "игры", "игр")).toBe("1 игра");
    expect(plural(3, "игра", "игры", "игр")).toBe("3 игры");
    expect(plural(12, "игра", "игры", "игр")).toBe("12 игр");
    expect(plural(21, "игра", "игры", "игр")).toBe("21 игра");
  });

  it("builds a scoped catalog query and omits empty filters", () => {
    const query = new URLSearchParams(buildCatalogQuery({ communityKey: "camp-a", search: " Brass ",
      players: 4, types: ["Strategy"], categories: [1021], sort: "name" }));
    expect(Object.fromEntries(query)).toEqual({ community: "camp-a", sort: "name", search: "Brass",
      players: "4", types: "Strategy", categories: "1021" });
  });

  it("toggles filter values without mutating the source", () => {
    const source = [1, 2];
    expect(toggleValue(source, 2)).toEqual([1]);
    expect(toggleValue(source, 3)).toEqual([1, 2, 3]);
    expect(source).toEqual([1, 2]);
  });

  it("allows explicit selection only for overridable skipped imports", () => {
    const item = (overrides: Partial<ImportDraftItem>): ImportDraftItem => ({ bggId: 1,
      itemType: "BaseGame", snapshot: { name: "Game" }, selectedByDefault: false,
      isOverridable: false, ...overrides });
    expect(isImportItemSelectable(item({ skipReason: "AlreadyAddedManually" }))).toBe(false);
    expect(isImportItemSelectable(item({ skipReason: "AlreadyInBaseCollection", isOverridable: true }))).toBe(true);
    expect(defaultImportSelection([item({ selectedByDefault: true }),
      item({ bggId: 2, selectedByDefault: true, skipReason: "AlreadyAddedManually" })]))
      .toEqual(new Set(["BaseGame-1"]));
  });
});
