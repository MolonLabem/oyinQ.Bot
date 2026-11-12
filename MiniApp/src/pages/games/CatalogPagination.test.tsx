// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn() }, successEventName: "success" }));
import { api } from "../../api/client";
import type { GameListItem } from "../../api/types";
import { GamesPage } from "./GamesPage";

const game = (id: number): GameListItem => ({ bggId: id, name: `Игра ${id}`, type: "Other", typeName: "Другое", typeNames: [], availabilitySummary: "Есть в клубе", isDefinitelyAvailable: true, needsProviderCoordination: false });
const community = { key: "club", mode: "Club" as const, name: "Клуб", timeZoneId: "UTC" };
let host: HTMLDivElement; let root: Root; let items: GameListItem[];
const cards = () => [...host.querySelectorAll(".catalog-card .game-card-title")].map(x => x.textContent);
const pageNumber = () => host.querySelector<HTMLSelectElement>('[aria-label="Страница каталога"]')?.value;
const pager = () => host.querySelector('nav[aria-label="Страницы каталога"]');
async function click(label: string) { await act(async () => host.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!.click()); }
async function size(value: string) {
  await act(async () => { const select = host.querySelector<HTMLSelectElement>('[aria-label="Игр на странице"]')!; select.value = value; select.dispatchEvent(new Event("change", { bubbles: true })); });
}
beforeEach(() => {
  vi.clearAllMocks(); vi.useFakeTimers(); sessionStorage.clear();
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  window.scrollTo = vi.fn(); HTMLElement.prototype.scrollIntoView = vi.fn();
  items = Array.from({ length: 22 }, (_, i) => game(i + 1));
  items[9].expansions = [{ bggId: 101, name: "Дополнение" }];
  items.push({ ...game(101), name: "Дополнение" }, { ...game(201), name: "Отдельное дополнение" });
  vi.mocked(api).mockImplementation(async path => {
    const url = new URL(path, "https://example.test");
    if (url.pathname.startsWith("/catalog/")) return { ...game(Number(url.pathname.split("/").at(-1))), categories: [], mechanics: [], expansions: [], availability: { providers: [] } };
    const filtered = url.searchParams.get("search") ? items.filter(x => x.name.includes(url.searchParams.get("search")!)) : items;
    return { items: filtered, total: filtered.length, filters: { types: [], categories: [] } };
  });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.useRealTimers(); vi.restoreAllMocks(); });

it("pages visible game groups, keeps expansions on the same page, and preserves the page after details", async () => {
  await act(async () => root.render(<GamesPage community={community} bggAvailable={false} />));
  expect(cards()).toHaveLength(10); expect(cards().at(-1)).toBe("Игра 10");
  expect(pageNumber()).toBe("1"); expect(pager()?.textContent).toContain("из 3");
  expect(host.querySelector(".collection-expansions")?.textContent).toContain("Дополнение");
  await act(async () => host.querySelector<HTMLButtonElement>(".catalog-expansion-option")!.click());
  expect(host.querySelector(".game-overview h1")?.textContent).toBe("Игра 101");
  await click("Назад"); expect(cards().at(-1)).toBe("Игра 10");
  await click("Следующая страница"); expect(cards()[0]).toBe("Игра 11"); expect(cards()).toHaveLength(10);
  expect(document.activeElement).toBe(host.querySelector(".catalog-results"));
  await act(async () => host.querySelector<HTMLButtonElement>(".catalog-card")!.click());
  await click("Назад"); expect(cards()[0]).toBe("Игра 11");
  await click("Следующая страница"); expect(cards()).toEqual(["Игра 21", "Игра 22", "Отдельное дополнение"]);
  expect(host.querySelector<HTMLButtonElement>('[aria-label="Следующая страница"]')!.disabled).toBe(true);
  await click("Предыдущая страница"); expect(cards()[0]).toBe("Игра 11");
  await act(async () => { const select = host.querySelector<HTMLSelectElement>('[aria-label="Страница каталога"]')!; select.value = "1"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  expect(cards()[0]).toBe("Игра 1");
});

it("changes page size, resets to the first page and remembers the chosen size", async () => {
  await act(async () => root.render(<GamesPage community={community} bggAvailable={false} />));
  await click("Следующая страница"); await size("20");
  expect(cards()).toHaveLength(20); expect(cards()[0]).toBe("Игра 1");
  expect(pageNumber()).toBe("1"); expect(pager()?.textContent).toContain("из 2");
  await size("50"); expect(cards()).toHaveLength(23); expect(pager()).toBeNull();
  await act(async () => root.render(<GamesPage community={{ ...community, key: "other" }} bggAvailable={false} />));
  expect(cards()).toHaveLength(23); expect(sessionStorage.getItem("oyinq-catalog-page-size")).toBe("50");
});

it("searches the full collection and resets pagination when the search or sort changes", async () => {
  await act(async () => root.render(<GamesPage community={community} bggAvailable={false} />));
  await click("Следующая страница");
  await act(async () => { const input = host.querySelector<HTMLInputElement>('input[type="search"]')!; Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, "Игра 22"); input.dispatchEvent(new Event("input", { bubbles: true })); await vi.advanceTimersByTimeAsync(410); });
  expect(cards()).toEqual(["Игра 22"]); expect(pager()).toBeNull();
  await click("Очистить поиск"); await act(async () => { await vi.advanceTimersByTimeAsync(410); });
  expect(cards()[0]).toBe("Игра 1");
  await click("Следующая страница");
  await act(async () => { const select = host.querySelector<HTMLSelectElement>(".catalog-sort select")!; select.value = "popular"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  expect(cards()[0]).toBe("Игра 1"); expect(pageNumber()).toBe("1"); expect(pager()?.textContent).toContain("из 3");
});

it("clamps the page after a collection shrinks and keeps that page when games return", async () => {
  await act(async () => root.render(<GamesPage community={community} bggAvailable={false} />));
  await click("Следующая страница"); await click("Следующая страница");
  const original = items; items = items.slice(0, 11);
  await act(async () => window.dispatchEvent(new Event("focus")));
  expect(cards()).toEqual(["Игра 11"]); expect(pageNumber()).toBe("2"); expect(pager()?.textContent).toContain("из 2");
  items = original; await act(async () => window.dispatchEvent(new Event("focus")));
  expect(cards()[0]).toBe("Игра 11"); expect(pageNumber()).toBe("2"); expect(pager()?.textContent).toContain("из 3");
});
