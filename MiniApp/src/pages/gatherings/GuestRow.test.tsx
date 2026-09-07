import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { GuestRow } from "./GuestRow";

const actions = { rename: async () => true, remove: async () => true };
describe("GuestRow", () => {
  it("makes the name the edit control and labels the compact delete control", () => {
    const markup = renderToStaticMarkup(<GuestRow name="Друг Антона" editable busy={false} {...actions} />);
    expect(markup).toContain('aria-label="Изменить имя гостя Друг Антона"');
    expect(markup).toContain('aria-label="Удалить гостя Друг Антона"');
    expect(markup).toContain("Гость");
    expect(markup).not.toContain(">Изменить</button>");
    expect(markup).not.toContain(">Удалить</button>");
  });
  it("keeps guest names readable without exposing organizer controls", () => {
    const markup = renderToStaticMarkup(<GuestRow name="Друг Антона" editable={false} busy={false} {...actions} />);
    expect(markup).toContain("Друг Антона");
    expect(markup).not.toContain("<button");
  });
  it("disables both actions while another mutation is pending", () => {
    const markup = renderToStaticMarkup(<GuestRow name="Гость" editable busy {...actions} />);
    expect(markup.match(/disabled=""/g)).toHaveLength(2);
  });
});
