import { describe, expect, it } from "vitest";
import { plural } from "./format";
import { buildCatalogQuery, toggleValue } from "./catalogQuery";
import { defaultImportSelection, isImportItemSelectable } from "../pages/camp/importSelection";
import type { ImportDraftItem } from "../api/types";
import { fullscreenLabel, registrationSubmitEnabled, toggleRegistrationDate } from "../pages/camp/registrationLogic";

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

  it("adds, sorts, and removes exact registration dates immutably", () => {
    const source = ["2026-08-31"];
    expect(toggleRegistrationDate(source, "2026-08-29")).toEqual(["2026-08-29", "2026-08-31"]);
    expect(toggleRegistrationDate(source, "2026-08-31")).toEqual([]);
    expect(source).toEqual(["2026-08-31"]);
  });

  it("requires city, an exact date, and an active camp", () => {
    expect(registrationSubmitEnabled("Астана", ["2026-08-29"], "Active")).toBe(true);
    expect(registrationSubmitEnabled(" ", ["2026-08-29"], "Active")).toBe(false);
    expect(registrationSubmitEnabled("Астана", [], "Active")).toBe(false);
    expect(registrationSubmitEnabled("Астана", ["2026-08-29"], "Draft")).toBe(false);
  });

  it("shows reversible fullscreen labels", () => {
    expect(fullscreenLabel(false)).toBe("Развернуть на весь экран");
    expect(fullscreenLabel(true)).toBe("Свернуть");
  });
});
