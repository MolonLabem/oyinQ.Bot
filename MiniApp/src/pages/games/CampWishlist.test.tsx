// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn(() => () => {}), success: vi.fn(() => window.dispatchEvent(new Event("success"))) }, successEventName: "success" }));
import { api } from "../../api/client";
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
  expect(host.querySelector("dialog")).toBeNull(); expect(host.textContent).toContain("Найдено игр: 48");
  await click("Нужна коробка"); expect(api).toHaveBeenLastCalledWith(expect.stringContaining("needsBox=true"));
  await click("Дальше"); expect(api).toHaveBeenLastCalledWith(expect.stringContaining("page=2"));
  await click("Мои"); expect(api).toHaveBeenCalledWith(expect.stringContaining("mode=mine"));
  expect(api).toHaveBeenCalledWith(expect.stringContaining("view=outgoing"));
});

it("keeps loaded cards through refresh and error, and never posts during reads", async () => {
  await mount(); let reject!: (e: Error) => void;
  vi.mocked(api).mockImplementationOnce(() => new Promise((_, r) => { reject = r; }));
  await click("↻ Обновить список"); expect(host.textContent).toContain(game.game.name); expect(host.textContent).toContain("Обновляем…");
  await act(async () => reject(new Error("Нет сети")));
  expect(host.textContent).toContain(game.game.name); expect(host.textContent).toContain("Нет сети");
  expect(vi.mocked(api).mock.calls.every(call => !call[1])).toBe(true);
});

it("game to participant to game and back retains list mode and filters", async () => {
  await mount(); await click("Нужна коробка"); await click(game.game.name);
  expect(visible("h2")[0].textContent).toBe(game.game.name);
  await click("Виктор"); expect(visible("h2")[0].textContent).toBe("Виктор");
  await click(game.game.name); await click("← Назад"); expect(visible("h2")[0].textContent).toBe("Виктор");
  await click("← Назад"); await click("← Назад");
  expect(visible('[aria-pressed="true"]')[0].textContent).toContain("Нужна коробка");
  expect(visible('[aria-selected="true"]')[0].textContent).toBe("Все");
});

it("switching Camp cannot display an old delayed response", async () => {
  let old!: (value: unknown) => void;
  vi.mocked(api).mockImplementationOnce(() => new Promise(resolve => { old = resolve; }));
  await mount();
  await act(async () => root.render(<CampWishlist key="next" community={{ ...community, key: "next", name: "Другой кэмп" }} bggAvailable={false} />));
  await act(async () => old({ ...result, items: [{ ...game, game: { ...game.game, name: "Чужая игра" } }] }));
  expect(host.textContent).not.toContain("Чужая игра"); expect(host.textContent).toContain("Другой кэмп");
});
