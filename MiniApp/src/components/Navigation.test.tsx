import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { Navigation } from "./Navigation";
import { SegmentedControl } from "./Ui";

describe("navigation patterns", () => {
  it("renders decorative monochrome SVG icons while keeping text labels", () => {
    const markup = renderToStaticMarkup(<Navigation active="games" onChange={() => {}} tabs={[
      { id: "gatherings", label: "Сборы", icon: "gatherings" },
      { id: "games", label: "Игры", icon: "games" },
      { id: "profile", label: "Профиль", icon: "profile" }
    ]} />);

    expect(markup.match(/class="nav-icon"/g)).toHaveLength(3);
    expect(markup.match(/aria-hidden="true"/g)).toHaveLength(3);
    expect(markup).toContain('aria-current="page"');
    expect(markup).toContain("Сборы");
    expect(markup).not.toContain("🎲");
  });

  it("uses pressed buttons rather than tab semantics for a secondary filter", () => {
    const markup = renderToStaticMarkup(<SegmentedControl label="Фильтр истории" active="completed" onChange={() => {}} items={[
      { id: "all", label: "Все" }, { id: "completed", label: "Завершены" }, { id: "cancelled", label: "Отменены" }
    ]} />);

    expect(markup).toContain('role="group"');
    expect(markup).toContain('aria-pressed="true"');
    expect(markup).not.toContain('role="tab"');
  });
});
