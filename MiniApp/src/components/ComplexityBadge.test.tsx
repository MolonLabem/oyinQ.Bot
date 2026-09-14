import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
import type { ComplexityInfo } from "../api/types";
vi.mock("../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
import { ComplexityBadge, ComplexityDetails } from "./ComplexityBadge";
import { GameMeta } from "./GamePicker";
import { CatalogGameList } from "../pages/games/GamesPage";

const info: ComplexityInfo = { weight: 2.1234, level: "MediumHeavy", displayName: "Сложная", cssClass: "complexity-hard" };
describe("complexity presentation", () => {
  it("uses the server category even when average weight differs", () => {
    const markup = renderToStaticMarkup(<ComplexityBadge info={info} />);
    expect(markup).toContain("complexity-hard");
    expect(markup).toContain("Сложная");
    expect(markup).not.toContain("2.1234");
  });
  it("omits unknown complexity and formats details without changing the input", () => {
    expect(renderToStaticMarkup(<><ComplexityBadge /><ComplexityDetails info={null} /></>)).toBe("");
    expect(renderToStaticMarkup(<ComplexityDetails info={info} />)).toContain("2,12 · Сложная");
    expect(info.weight).toBe(2.1234);
    expect(renderToStaticMarkup(<ComplexityDetails info={{ ...info, weight: null }} />)).toContain("Сложность: Сложная");
  });
  it("shows complexity even when the selection or personal snapshot has no other metadata", () => {
    expect(renderToStaticMarkup(<GameMeta compact game={{ bggId: 42, name: "Game", expansions: [], complexityInfo: info }} />)).toContain("complexity-hard");
  });
  it("shows complexity while browsing catalog and nested expansions", () => {
    const markup = renderToStaticMarkup(<CatalogGameList club open={() => {}} items={[{
      bggId: 42, name: "Game", type: "Other", typeName: "", typeNames: [],
      availabilitySummary: "", isDefinitelyAvailable: false, needsProviderCoordination: false,
      complexityInfo: info, expansions: [{ bggId: 43, name: "Expansion", complexityInfo: info }]
    }]} />);
    expect(markup.match(/complexity-hard/g)).toHaveLength(2);
  });
});
