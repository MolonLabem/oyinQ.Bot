// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: () => () => {} }, successEventName: "success" }));
import { api } from "../../api/client";
import { GamesPage } from "./GamesPage";

let root: Root; let host: HTMLDivElement;
const response = { items: [], total: 0, filters: { categories: [], types: [] } };
const render = () => act(async () => root.render(<GamesPage community={{ key: "club", mode: "Club", name: "Клуб", timeZoneId: "UTC" }} bggAvailable />));
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.clearAllMocks(); sessionStorage.clear();
  vi.spyOn(window, "scrollTo").mockImplementation(() => {}); HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.mocked(api).mockResolvedValue(response);
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); vi.useRealTimers(); });

it("defaults to BGG rating and offers exactly rating, popularity and alphabetical queries", async () => {
  await render();
  const select = host.querySelector<HTMLSelectElement>(".catalog-sort select")!;
  expect(select.value).toBe("rating");
  expect([...select.options].map(x => x.value)).toEqual(["rating", "popular", "name"]);
  for (const sort of ["popular", "name", "rating"]) {
    await act(async () => { select.value = sort; select.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(api).toHaveBeenLastCalledWith(expect.stringContaining(`sort=${sort}`));
  }
});

it("refreshes pending ratings and stops polling once the background fetch finishes", async () => {
  vi.useFakeTimers();
  vi.mocked(api).mockResolvedValueOnce({ ...response, ratingsPending: true }).mockResolvedValue(response);
  await render();
  expect(host.textContent).toContain("Загружаем рейтинг BGG");
  await act(async () => vi.advanceTimersByTimeAsync(5000));
  expect(api).toHaveBeenCalledTimes(2);
  expect(host.textContent).not.toContain("Загружаем рейтинг BGG");
  await act(async () => vi.advanceTimersByTimeAsync(20000));
  expect(api).toHaveBeenCalledTimes(2);
});
