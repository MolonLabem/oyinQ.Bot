import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
import type { GameListItem } from "../../api/types";
vi.mock("../../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
import { CatalogGameList } from "./GamesPage";

const game = (bggId: number, name: string, expansions: GameListItem["expansions"] = []): GameListItem => ({
  bggId, name, type: "Other", typeName: "Другое", typeNames: [], availabilitySummary: "Есть в клубе",
  isDefinitelyAvailable: true, needsProviderCoordination: false, expansions
});

describe("catalog collection grouping", () => {
  it("shows an expansion once under its base and leaves orphans visible", () => {
    const markup = renderToStaticMarkup(<CatalogGameList club open={() => {}} items={[
      game(1, "Базовая игра", [{ bggId: 2, name: "Дополнение" }]), game(2, "Дополнение"), game(3, "Отдельное дополнение")
    ]} />);
    expect(markup.match(/class="card catalog-card"/g)).toHaveLength(2);
    expect(markup).toContain('<summary>Дополнения (1)</summary>');
    expect(markup).toContain('<button class="ghost">Дополнение</button>');
    expect(markup).toContain('Отдельное дополнение');
    expect(markup).not.toContain('availability success');
  });
  it("opens nested stored expansions during search even without an independent detail row", () => {
    const markup = renderToStaticMarkup(<CatalogGameList club searching open={() => {}}
      items={[game(1, "База", [{ bggId: 2, name: "Найденное дополнение" }])]} />);
    expect(markup).toContain('<details class="collection-expansions" open="">');
    expect(markup).toContain('<li>Найденное дополнение</li>');
  });
});
