import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
import type { GameListItem } from "../../api/types";
vi.mock("../../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
import { CatalogGameList, ProviderMultiSelect } from "./GamesPage";

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
    expect(markup).toContain('catalog-game-group has-expansions');
    expect(markup).toContain('Часть этой карточки');
    expect(markup).toContain('<button class="catalog-expansion-option"');
    expect(markup).toContain('Открыть');
    expect(markup).toContain('Отдельное дополнение');
    expect(markup).not.toContain('availability success');
  });
  it("opens nested stored expansions during search even without an independent detail row", () => {
    const markup = renderToStaticMarkup(<CatalogGameList club searching open={() => {}}
      items={[game(1, "База", [{ bggId: 2, name: "Найденное дополнение" }])]} />);
    expect(markup).toContain('<details class="collection-expansions" open="">');
    expect(markup).toContain('Найденное дополнение');
    expect(markup).toContain('Дополнение</small>');
  });
  it("показывает мультиселект участников с сохранённым выбором", () => {
    const markup = renderToStaticMarkup(<ProviderMultiSelect values={[
      { participantId: 1, displayName: "Пётр" }, { participantId: 2, displayName: "Анна" }
    ]} selected={[2]} toggle={() => {}} clear={() => {}} />);
    expect(markup.match(/type="checkbox"/g)).toHaveLength(2);
    expect(markup).toContain("Пётр");
    expect(markup).toContain("Анна");
    expect(markup).toContain('checked=""');
    expect(markup).toContain("Сбросить выбор · 1");
  });
});
