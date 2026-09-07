import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";

vi.mock("../../hooks/useAsync", () => ({
  useAsync: () => ({ data: [], loading: false, reload: vi.fn() })
}));
vi.mock("../../telegram/webApp", () => ({
  telegram: { success: vi.fn() }, successEventName: "success"
}));
import { CreateGathering } from "./GatheringsPage";

describe("gathering creation flow", () => {
  it("offers one direct create action without an intermediate review step", () => {
    const markup = renderToStaticMarkup(<CreateGathering
      community={{ key: "club", name: "Клуб", mode: "Club", timeZoneId: "UTC" }}
      bggAvailable onDone={() => {}} editRegistration={() => {}} />);

    expect(markup).toContain(">Создать сбор</button>");
    expect(markup).not.toContain("Проверить сбор");
    expect(markup).not.toContain("Подтвердить и создать");
  });
});
