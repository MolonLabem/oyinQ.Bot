// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn(() => () => {}), success: vi.fn(() => window.dispatchEvent(new Event("success"))) }, successEventName: "success" }));
import { api } from "../../api/client";
import { telegram } from "../../telegram/webApp";
import { CampWishlist } from "./CampWishlist";

let host: HTMLDivElement; let root: Root;
const community = { key: "camp", name: "Осенний кэмп", mode: "Camp" as const, timeZoneId: "UTC" };
const game = { game: { bggId: 42, name: "Немезида с очень длинным названием", originalName: "Nemesis", expansions: [] }, interestedParticipants: 8, otherInterested: 8, isOwned: false, isWished: true, confirmed: false, boxSummary: "Коробку пока не нашли", scheduledGatherings: 0 };
const person = { id: "owner", name: "Виктор", dates: ["2026-09-24"] };
const result = { items: [game], total: 48, suggestions: 3, hasMore: true, canAct: true, shareCollection: false, shareWishes: false, myDates: person.dates, updatedAt: "2026-09-23T12:00:00Z" };
const detail = { item: game, interested: [person], anonymousCount: 7, owners: [], requests: [], canAct: true, myDates: person.dates };
const visible = (selector: string) => [...host.querySelectorAll<HTMLElement>(selector)].filter(x => !x.closest("[hidden]"));
async function click(text: string) { const button = visible("button").find(x => x.textContent?.trim() === text || x.querySelector("strong")?.textContent === text); expect(button, text).toBeTruthy(); await act(async () => button!.click()); }
async function mount() { await act(async () => root.render(<CampWishlist community={community} bggAvailable={false} />)); }
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.clearAllMocks(); sessionStorage.clear();
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute("open", ""); }; HTMLDialogElement.prototype.close = function () { this.removeAttribute("open"); };
  HTMLElement.prototype.scrollIntoView = vi.fn(); vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  vi.mocked(api).mockImplementation(async path => {
    const u = new URL(path, "https://test");
    if (u.searchParams.has("game")) return detail;
    if (u.searchParams.get("view") === "profile") return { person, isMe: false, wishesVisible: true, items: [game], total: 1, hasMore: false };
    if (u.searchParams.has("view")) return [];
    return result;
  });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });

it("defaults to all, keeps picker closed, sends filters and pagination to the server", async () => {
  await mount(); expect(visible('[aria-selected="true"]')[0].textContent).toBe("Все");
  expect(host.querySelector("dialog")).toBeNull(); expect(host.textContent).toContain("Игры: 48");
  await applyFilter("Нужна коробка"); expect(api).toHaveBeenLastCalledWith(expect.stringContaining("needsBox=true"));
  await click("Дальше"); expect(api).toHaveBeenLastCalledWith(expect.stringContaining("page=2"));
  await click("Мои"); expect(api).toHaveBeenCalledWith(expect.stringContaining("mode=mine"));
  expect(api).toHaveBeenCalledWith(expect.stringContaining("view=outgoing"));
});

it("keeps loaded cards through refresh and error, and never posts during reads", async () => {
  await mount(); let reject!: (e: Error) => void;
  vi.mocked(api).mockImplementationOnce(() => new Promise((_, r) => { reject = r; }));
  await click("↻ Обновить"); expect(host.textContent).toContain(game.game.name); expect(host.textContent).toContain("Обновляем…");
  await act(async () => reject(new Error("Нет сети")));
  expect(host.textContent).toContain(game.game.name); expect(host.textContent).toContain("Нет сети");
  expect(vi.mocked(api).mock.calls.every(call => !call[1])).toBe(true);
});

it("game to participant to game and back retains list mode and filters", async () => {
  await mount(); await applyFilter("Нужна коробка"); await click(game.game.name);
  expect(visible("h2")[0].textContent).toBe(game.game.name);
  await click("Виктор"); expect(visible("h2")[0].textContent).toBe("Виктор");
  await click(game.game.name); await click("← Назад"); expect(visible("h2")[0].textContent).toBe("Виктор");
  await click("← Назад"); await click("← Назад");
  expect(visible('[aria-label="Убрать: Нужна коробка"]')[0].textContent).toContain("Нужна коробка");
  expect(visible('[aria-selected="true"]')[0].textContent).toBe("Все");
});

it("switching Camp cannot display an old delayed response", async () => {
  let old!: (value: unknown) => void;
  vi.mocked(api).mockImplementationOnce(() => new Promise(resolve => { old = resolve; }));
  await mount();
  await act(async () => root.render(<CampWishlist key="next" community={{ ...community, key: "next", name: "Другой кэмп" }} bggAvailable={false} />));
  await act(async () => old({ ...result, items: [{ ...game, game: { ...game.game, name: "Чужая игра" } }] }));
  expect(host.textContent).not.toContain("Чужая игра"); expect(api).toHaveBeenCalledWith(expect.stringContaining("community=next"));
});

async function toggleFilter(label: string) { const input = [...host.querySelectorAll<HTMLInputElement>("dialog input")].find(x => x.parentElement?.textContent === label)!; expect(input).toBeTruthy(); await act(async () => input.click()); }
async function applyFilter(label: string) { await click(visible("button").some(x => x.textContent === "Фильтры •") ? "Фильтры •" : "Фильтры"); await toggleFilter(label); await click("Применить"); }

it("keeps draft filters unapplied on dismiss, shows chips after apply and clears hidden mode filters", async () => {
  await mount(); const requests = vi.mocked(api).mock.calls.length;
  await click("Фильтры"); await toggleFilter("Нужна коробка");
  await act(async () => host.querySelector<HTMLButtonElement>('button[aria-label="Закрыть без применения"]')!.click());
  expect(vi.mocked(api).mock.calls.length).toBe(requests);
  expect(host.querySelector('[aria-label="Убрать: Нужна коробка"]')).toBeNull();
  await applyFilter("Есть у меня"); expect(host.querySelector('[aria-label="Убрать: Есть у меня"]')).not.toBeNull();
  await click("Что взять"); expect(api).toHaveBeenCalledWith(expect.stringMatching(/mode=bring.*owned=false/));
  await click("Фильтры"); expect([...host.querySelectorAll("dialog label")].some(x => x.textContent === "Есть у меня")).toBe(false);
  await toggleFilter("Включая те, что не беру"); await click("Применить");
  await click("Все"); expect(api).toHaveBeenCalledWith(expect.stringMatching(/mode=all.*showDeclined=false/));
});

it("resets filter draft and sorting only when applied and allows removing individual chips", async () => {
  await mount(); await applyFilter("Нужна коробка");
  await click("Фильтры •"); const select = host.querySelector<HTMLSelectElement>("dialog select")!;
  await act(async () => { select.value = "name"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  await click("Применить"); expect(host.textContent).toContain("По названию ×");
  await click("Нужна коробка ×"); expect(api).toHaveBeenCalledWith(expect.stringMatching(/needsBox=false.*sort=name/));
  await click("Фильтры •"); await click("Сбросить"); expect(host.textContent).toContain("По названию ×");
  await click("Применить"); expect(host.querySelector('[aria-label="Убрать сортировку по названию"]')).toBeNull();
});

function ownGames(status?: string, dates?: string[]) {
  let mine = { ...game, isOwned: true, myStatus: status, myAllAttendanceDays: !dates, myAvailableDates: dates };
  const neighbour = { ...game, game: { ...game.game, bggId: 43, name: "Другая игра" }, isOwned: true };
  const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => {
    const url = new URL(path, "https://test");
    if (init?.method) {
      const body = JSON.parse(String(init.body));
      const next = body.commitment ?? (body.action === "decline" ? "declined" : undefined);
      mine = { ...mine, myStatus: next, confirmed: next === "Bringing", boxSummary: next === "Bringing" ? "Вы точно привезёте · 24 сентября" : "Коробку пока не нашли" };
      return {};
    }
    if (url.searchParams.has("game")) return { ...detail, item: { ...mine } };
    if (!url.searchParams.has("view")) {
      const hidden = url.searchParams.get("needsBox") === "true" && mine.confirmed || url.searchParams.get("mode") === "bring" && mine.myStatus === "declined";
      return { ...result, items: hidden ? [neighbour] : [{ ...mine }, neighbour], total: hidden ? 47 : 48 };
    }
    return previous(path, init);
  });
}
function box() { return visible(".box-control select")[0] as HTMLSelectElement; }
async function choose(value: string) { await act(async () => { const select = box(); select.value = value; select.dispatchEvent(new Event("change", { bubbles: true })); }); }
const writes = () => vi.mocked(api).mock.calls.filter(c => c[1]?.method);

it.each(["Все", "Мои", "Что взять"])("saves own box in place in %s and reloads the summary without resetting page or mode", async mode => {
  ownGames(); await mount(); if (mode !== "Все") await click(mode);
  await click("Дальше"); expect(host.textContent).toContain("Страница 2");
  expect(box().selectedOptions[0].textContent).toBe("Предложить свою игру");
  await choose("Bringing");
  expect(writes()).toHaveLength(1);
  expect(writes()[0][0]).toBe("/camp/contributions/BaseGame/42/commitment");
  expect(JSON.parse(String(writes()[0][1]?.body))).toEqual({ communityKey: "camp", commitment: "Bringing", allAttendanceDays: true });
  expect(box().value).toBe("Bringing"); expect(visible(".wish-card")[0].textContent).toContain("Вы точно привезёте");
  expect(visible(".wish-card")[0].textContent).not.toContain("Коробку пока не нашли");
  expect(visible("h2")).toHaveLength(0); expect(host.textContent).toContain("Страница 2");
  expect(visible('[aria-selected="true"]')[0].textContent).toBe(mode);
  await choose("Bringing"); expect(writes()).toHaveLength(1);
});

it("opening or dismissing the native selector does not write; pending save locks only that game", async () => {
  ownGames(); await mount();
  await act(async () => { box().focus(); box().click(); box().dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })); });
  expect(writes()).toHaveLength(0);
  let finish!: () => void; const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => { if (init?.method) await new Promise<void>(resolve => { finish = resolve; }); return previous(path, init); });
  await choose("Available"); expect(box().disabled).toBe(true);
  expect((visible(".box-control select")[1] as HTMLSelectElement).disabled).toBe(false);
  await choose("Bringing"); expect(writes()).toHaveLength(1);
  await act(async () => finish()); expect(box().value).toBe("Available");
});

it("retains confirmed value on failure and retries the chosen status locally", async () => {
  ownGames("Available", ["2026-09-24"]); await mount();
  const previous = vi.mocked(api).getMockImplementation()!; let fail = true;
  vi.mocked(api).mockImplementation(async (path, init) => { if (init?.method && fail) throw new Error("Нет сети"); return previous(path, init); });
  await choose("Bringing"); expect(box().value).toBe("Available"); expect(visible(".wish-card")[0].textContent).toContain("Нет сети");
  fail = false; await click("Повторить"); expect(box().value).toBe("Bringing");
  expect(JSON.parse(String(writes()[1][1]?.body))).toEqual({ communityKey: "camp", commitment: "Bringing" });
  expect(visible(".wish-card")[0].textContent).toContain("Только 24 сентября");
});

it("retries only the projection after an accepted save and a failed reload", async () => {
  ownGames(); await mount(); const previous = vi.mocked(api).getMockImplementation()!; let failed = true;
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (!init?.method && !path.includes("view=") && failed) throw new Error("Нет сети");
    return previous(path, init);
  });
  await choose("Bringing"); expect(host.textContent).toContain("Решение сохранено. Не удалось обновить список.");
  failed = false; const retry = host.querySelector<HTMLButtonElement>(".box-control .notice button")!;
  await act(async () => retry.click()); expect(box().value).toBe("Bringing"); expect(writes()).toHaveLength(1);
});

it("uses the shared selector in details, preserving date intent and distinguishing refusal from withdrawal", async () => {
  ownGames("Available", ["2026-09-24"]); await mount(); await click(game.game.name);
  expect(box().value).toBe("Available"); expect(host.querySelector('section[data-wish-section="box"] select')).not.toBeNull();
  await choose("declined"); expect(box().value).toBe("declined");
  await choose(""); expect(box().value).toBe("");
  expect(writes().map(c => JSON.parse(String(c[1]?.body)))).toEqual([{ action: "decline", bggId: 42 }, { action: "withdraw", bggId: 42 }]);
  expect(visible("h2")[0].textContent).toBe(game.game.name);
});

it("removes a newly confirmed game from the box filter and anchors its next neighbour", async () => {
  ownGames(); await mount(); await applyFilter("Нужна коробка");
  const rect = vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
    return { top: this.dataset.boxGame === "42" ? 160 : host.querySelector('[data-box-game="42"]') ? 450 : 100 } as DOMRect;
  });
  vi.spyOn(window, "scrollY", "get").mockReturnValue(600); vi.mocked(window.scrollTo).mockClear();
  let finish!: () => void; const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => { if (init?.method) await new Promise<void>(resolve => { finish = resolve; }); return previous(path, init); });
  await choose("Bringing");
  await act(async () => window.dispatchEvent(new Event("focus"))); // Reads before the write commits must not consume the anchor.
  expect(window.scrollTo).not.toHaveBeenCalled();
  await act(async () => finish());
  expect(host.querySelector('[data-box-game="42"]')).toBeNull(); expect(host.textContent).toContain("Игры: 47");
  expect(host.textContent).toContain("Решение сохранено. Игра больше не входит");
  expect(window.scrollTo).toHaveBeenCalledWith(0, 250);
  expect(visible('[aria-label="Убрать: Нужна коробка"]')).toHaveLength(1); rect.mockRestore();
});

it("ignores a late mutation result after switching Camp", async () => {
  ownGames(); await mount(); let finish!: () => void;
  const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => { if (init?.method) await new Promise<void>(resolve => { finish = resolve; }); return previous(path, init); });
  await choose("Bringing");
  await act(async () => root.render(<CampWishlist community={{ ...community, key: "next" }} bggAvailable={false} />));
  const before = vi.mocked(api).mock.calls.length;
  await act(async () => finish());
  expect(vi.mocked(api).mock.calls).toHaveLength(before); expect(telegram.success).not.toHaveBeenCalled(); expect(box().value).toBe("");
});
