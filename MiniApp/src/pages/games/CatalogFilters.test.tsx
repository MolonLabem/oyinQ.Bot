// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn(() => () => {}) }, successEventName: "test:success" }));
vi.mock("../../api/client", async original => ({ ...await original<typeof import("../../api/client")>(), api: vi.fn() }));
import { api } from "../../api/client";
import { telegram } from "../../telegram/webApp";
import { GamesPage } from "./GamesPage";
import type { CatalogResponse, Community } from "../../api/types";
import { filterStorageKey } from "../../app/catalogFilters";

let host: HTMLDivElement; let root: Root;
const community: Community = { key: "camp", mode: "Camp", name: "Кэмп", timeZoneId: "UTC" };
const options = { categories: Array.from({ length: 20 }, (_, i) => ({ bggId: i + 1, name: `Категория ${i + 1}` })), types: [{ key: "Strategy" as const, value: "Стратегии" }], providers: [{ participantId: 1, displayName: "Анна" }] };
const result = (total = 8): CatalogResponse => ({ total, items: total ? [{ bggId: 1, name: "Игра", type: "Strategy", typeName: "Стратегия", typeNames: [], availabilitySummary: "", isDefinitelyAvailable: false, needsProviderCoordination: false }] : [], filters: options });
async function mount(c = community) { await act(async () => root.render(<GamesPage community={c} bggAvailable={false} />)); }
async function click(text: string, inDialog = false) {
  const scope = inDialog ? host.querySelector("dialog")! : host;
  const el = Array.from(scope.querySelectorAll("button")).find(x => x.textContent?.trim() === text)!;
  expect(el, text).toBeTruthy(); await act(async () => el.click());
}
async function tick() { await act(async () => { await vi.advanceTimersByTimeAsync(310); }); }
const listCalls = () => vi.mocked(api).mock.calls.filter(([url]) => url.startsWith("/catalog?") && !url.includes("countOnly"));
const status = () => host.querySelector(".catalog-result-status")!.textContent;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.useFakeTimers(); vi.clearAllMocks(); sessionStorage.clear();
  Object.defineProperty(HTMLDialogElement.prototype, "showModal", { configurable: true, value() { this.open = true; } });
  Object.defineProperty(HTMLDialogElement.prototype, "close", { configurable: true, value() { this.open = false; } });
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  vi.mocked(api).mockImplementation(async path => path.includes("countOnly") ? { total: 3 } : result());
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.useRealTimers(); vi.restoreAllMocks(); });
describe("catalog panel", () => {
  it("separates section expansion, five-option preview, draft, apply and reset", async () => {
    await mount(); await click("Фильтры");
    const section = Array.from(host.querySelectorAll("details")).find(x => x.textContent?.includes("Категории"))!;
    expect(section.open).toBe(false); expect(section.querySelectorAll('input[type="checkbox"]')).toHaveLength(5);
    await act(async () => { section.open = true; });
    await click("Показать ещё · 15", true);
    expect(section.querySelectorAll('input[type="checkbox"]')).toHaveLength(20);
    await act(async () => section.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[8].click());
    await click("Свернуть", true);
    expect(section.querySelectorAll('input[type="checkbox"]')).toHaveLength(5);
    expect(section.textContent).toContain("Выбрано: 1");
    expect(section.querySelector('button[aria-label="Убрать: Категория 9"]')).not.toBeNull();
    expect(listCalls()).toHaveLength(1); await tick(); await click("Показать 3 игры", true);
    expect(host.querySelector("dialog")).toBeNull(); expect(listCalls().at(-1)![0]).toContain("categories=9");
    expect(host.textContent).toContain("Фильтры · 1");
    await click("Фильтры · 1"); await click("Сбросить", true);
    expect(host.querySelector(".catalog-applied")!.textContent).toContain("Категория 9");
    await act(async () => host.querySelector("dialog")!.dispatchEvent(new Event("cancel", { cancelable: true })));
    await click("Фильтры · 1"); expect(host.querySelector("dialog")!.textContent).toContain("Выбрано: 1");
    await act(async () => host.querySelector<HTMLButtonElement>('button[aria-label="Закрыть без применения"]')!.click());
    await act(async () => host.querySelector<HTMLButtonElement>('button[aria-label="Убрать фильтр: Категория 9"]')!.click());
    expect(listCalls().at(-1)![0]).not.toContain("categories");
  });
  it("ignores stale preview responses immediately and supports Telegram Back", async () => {
    let finish!: (value: { total: number }) => void;
    vi.mocked(api).mockImplementation(path => path.includes("countOnly") ? new Promise(resolve => { finish = resolve; }) : Promise.resolve(result()));
    await mount(); await click("Фильтры"); await tick();
    await click("4", true);
    await act(async () => finish({ total: 99 }));
    expect(host.querySelector("dialog")!.textContent).not.toContain("Показать 99");
    expect(host.querySelector("dialog")!.textContent).toContain("Считаем");
    await act(async () => vi.mocked(telegram.back).mock.calls.at(-1)![1]());
    expect(host.querySelector("dialog")).toBeNull(); expect(listCalls()).toHaveLength(1);
    await click("Фильтры"); expect(host.querySelector('button[aria-pressed="true"]')).toBeNull();
  });
  it("normalizes obsolete saved Club constraints and isolates community state", async () => {
    sessionStorage.setItem(filterStorageKey("club", "Club"), JSON.stringify({ availability: "confirmed", ownership: "participants", providers: [1], categories: [999] }));
    await mount({ ...community, key: "club", mode: "Club" });
    expect(listCalls().at(-1)![0]).not.toMatch(/availability|providers|ownership|categories/);
    await click("Фильтры"); expect(host.querySelector("dialog")!.textContent).not.toContain("Коробка на кэмпе");
    await mount(); expect(host.querySelector("dialog")).toBeNull();
    expect(listCalls().at(-1)![0]).toContain("community=camp");
    await click("Фильтры"); expect(host.querySelector("dialog")!.textContent).toContain("Коробка на кэмпе");
  });
  it("keeps errors distinct from empty results, retries and refreshes on focus", async () => {
    await mount();
    vi.mocked(api).mockRejectedValueOnce(new Error("Нет сети"));
    await act(async () => window.dispatchEvent(new Event("focus")));
    expect(status()).toContain("Не удалось"); expect(host.textContent).toContain("Нет сети"); expect(host.textContent).not.toContain("Ничего не найдено");
    vi.mocked(api).mockResolvedValue(result(0)); await click("Повторить"); expect(status()).toBe("0 игр");
  });
  it("does not display an old community's delayed list", async () => {
    let resolve!: (value: CatalogResponse) => void;
    vi.mocked(api).mockImplementation(path => path.includes("community=camp") ? new Promise(done => { resolve = done; }) : Promise.resolve(result(2)));
    await mount(); expect(status()).toContain("Обновляем"); await mount({ ...community, key: "other" });
    await act(async () => resolve(result(99))); expect(status()).toBe("2 игры");
  });
  it("applies without a preview count, keeps choices on list failure, and resets zero results", async () => {
    await mount(); await click("Фильтры"); await click("4", true);
    vi.mocked(api).mockRejectedValueOnce(new Error("Сбой подсчёта")); await tick();
    expect(host.querySelector("dialog")!.textContent).toContain("Не удалось посчитать");
    vi.mocked(api).mockRejectedValue(new Error("Нет сети")); await click("Показать игры", true);
    expect(status()).toContain("Не удалось"); expect(host.querySelector(".catalog-applied")!.textContent).toContain("Игроков: 4");
    vi.mocked(api).mockResolvedValue(result(0)); await click("Повторить");
    expect(status()).toBe("0 игр"); expect(host.textContent).toContain("Ничего не найдено");
    await click("Сбросить поиск и фильтры"); expect(listCalls().at(-1)![0]).not.toContain("players");
  });
  it("shows loading during Apply and ignores the older filter response", async () => {
    await mount(); await click("Фильтры"); await click("4", true); await tick();
    let old!: (value: CatalogResponse) => void;
    vi.mocked(api).mockImplementation(path => path.includes("players=4") ? new Promise(resolve => { old = resolve; }) : Promise.resolve(result(2)));
    await click("Показать 3 игры", true); expect(status()).toContain("Обновляем");
    await act(async () => host.querySelector<HTMLButtonElement>('button[aria-label="Убрать фильтр: Игроков: 4"]')!.click());
    await act(async () => old(result(99))); expect(status()).toBe("2 игры");
  });

  it("does not reuse the old count when draft returns to an earlier selection", async () => {
    await mount(); await click("Фильтры"); await tick();
    expect(host.querySelector("dialog")!.textContent).toContain("Показать 3 игры");
    await click("4", true); await click("4", true);
    expect(host.querySelector("dialog")!.textContent).not.toContain("Показать 3 игры");
    expect(host.querySelector("dialog")!.textContent).toContain("Считаем");
    await tick(); expect(host.querySelector("dialog")!.textContent).toContain("Показать 3 игры");
  });

});
