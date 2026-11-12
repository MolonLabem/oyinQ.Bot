import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
import type { GameListItem } from "../../api/types";
vi.mock("../../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
import { CatalogGameList } from "./GamesPage";
import { FilterChoices } from "./CatalogFilters";

const game = (bggId: number, name: string, expansions: GameListItem["expansions"] = []): GameListItem => ({
  bggId, name, type: "Other", typeName: "Другое", typeNames: [], availabilitySummary: "Есть в клубе",
  isDefinitelyAvailable: true, needsProviderCoordination: false, expansions
});

describe("catalog collection grouping", () => {
  it("keeps duration, rating and player facts accessible without the old type and details rows", () => {
    const markup = renderToStaticMarkup(<CatalogGameList open={() => {}} items={[{
      ...game(1, "Игра"), minPlayers: 2, maxPlayers: 4, minPlayTimeMinutes: 90, maxPlayTimeMinutes: 120, bggRating: 8.12
    }]} />);
    expect(markup).toContain("Длительность: "); expect(markup).toContain("90–120 мин");
    expect(markup).toContain("Игроки: "); expect(markup).toContain("2–4");
    expect(markup).toContain("8,1"); expect(markup).toContain("Рейтинг BGG: ");
    expect(markup).toContain("aria-describedby=");
    expect(markup).not.toContain("Другое"); expect(markup).not.toContain("Подробнее");
  });

  it("shows base capacity separately from the available expansion capacity", () => {
    const markup = renderToStaticMarkup(<CatalogGameList open={() => {}} items={[{
      ...game(1, "База", [{ bggId: 2, name: "Дополнение", minPlayers: 1, maxPlayers: 6 }]),
      minPlayers: 1, maxPlayers: 4, expansionPlayerRange: { minimum: 1, maximum: 6 }
    }]} />).replace(/<[^>]+>/g, "");
    expect(markup).toContain("Базовая игра: 1–4");
    expect(markup).toContain("С дополнениями: 1–6");
  });
  it("shows an expansion once under its base and leaves orphans visible", () => {
    const markup = renderToStaticMarkup(<CatalogGameList open={() => {}} items={[
      game(1, "Базовая игра", [{ bggId: 2, name: "Дополнение" }]), game(2, "Дополнение"), game(3, "Отдельное дополнение")
    ]} />);
    expect(markup.match(/class="card catalog-card"/g)).toHaveLength(2);
    expect(markup).toContain('catalog-game-group has-expansions');
    expect(markup).toContain('В коллекции');
    expect(markup).toContain('<button class="catalog-expansion-option"');
    expect(markup).toContain('Открыть');
    expect(markup).toContain('Отдельное дополнение');
    expect(markup).toContain('availability success');
    expect(markup).not.toContain('Подтверждено партий');
    expect(markup).not.toContain('class="tag-list"');
  });
  it("opens nested stored expansions during search even without an independent detail row", () => {
    const markup = renderToStaticMarkup(<CatalogGameList searching open={() => {}}
      items={[game(1, "База", [{ bggId: 2, name: "Найденное дополнение" }])]} />);
    expect(markup).toContain('<details class="collection-expansions" open="">');
    expect(markup).toContain('Найденное дополнение');
    expect(markup).toContain('Дополнение</small>');
  });
  it("показывает мультиселект участников с сохранённым выбором", () => {
    const markup = renderToStaticMarkup(<FilterChoices title="Кто может привезти" values={[
      { key: 1, value: "Пётр" }, { key: 2, value: "Анна" }
    ]} selected={[2]} change={() => {}} />);
    expect(markup.match(/type="checkbox"/g)).toHaveLength(2);
    expect(markup).toContain("Пётр");
    expect(markup).toContain("Анна");
    expect(markup).toContain('checked=""');
    expect(markup).toContain("Выбрано: 1");
  });
});
