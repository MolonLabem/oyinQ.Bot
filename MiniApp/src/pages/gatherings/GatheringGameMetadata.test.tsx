import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { GatheringBggLink, GatheringCollectionAction, GatheringTypeTag } from "./GatheringGameMetadata";

describe("gathering game metadata", () => {
  it("renders a concise type tag and canonical BGG action", () => {
    const markup = renderToStaticMarkup(<>
      <GatheringTypeTag typeName="Стратегия" />
      <GatheringBggLink bggUrl="https://boardgamegeek.com/boardgame/167791" />
    </>);

    expect(markup).toContain("Стратегия");
    expect(markup).toContain('href="https://boardgamegeek.com/boardgame/167791"');
    expect(markup).toContain("Открыть на BGG");
    expect(renderToStaticMarkup(<GatheringBggLink
      bggUrl="https://boardgamegeek.com/boardgame/167791" compact />)).toContain(">BGG</a>");
  });

  it("omits empty type and missing BGG link", () => {
    expect(renderToStaticMarkup(<>
      <GatheringTypeTag />
      <GatheringBggLink />
    </>)).toBe("");
  });

  it("offers collection navigation only for a canonical BGG ID", () => {
    expect(renderToStaticMarkup(<GatheringCollectionAction bggId={167791} open={() => undefined} />))
      .toContain("Посмотреть в коллекции");
    expect(renderToStaticMarkup(<GatheringCollectionAction open={() => undefined} />)).toBe("");
  });
});
