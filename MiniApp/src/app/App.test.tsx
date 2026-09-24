// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { Bootstrap } from "../api/types";

vi.mock("../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../telegram/webApp", () => ({ telegram: {
  onFullscreenChanged: () => () => {}, back: () => () => {}, success: vi.fn(),
}, successEventName: "success" }));
import { api } from "../api/client";
import { App } from "./App";

let host: HTMLDivElement;
let root: Root;
let discovery: () => Promise<Bootstrap>;
beforeEach(() => {
  vi.clearAllMocks();
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  Element.prototype.scrollIntoView = vi.fn();
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  localStorage.clear();
  localStorage.setItem("oyinq-community", "camp");
  history.replaceState({}, "", "/?tab=profile");
  discovery = () => Promise.reject(new Error("Telegram временно недоступен"));
  vi.mocked(api).mockImplementation(async path => {
    if (path === "/communities") return discovery();
    if (path === "/capabilities") return { boardGameGeekAvailable: false };
    if (path === "/profile") return { telegramDisplayName: "Игрок" };
    if (path === "/profile/gatherings") throw new Error("Расписание временно недоступно");
    if (path === "/profile/collection/") return [{ bggId: 42, itemType: "BaseGame", snapshot: { name: "Моя игра" } }];
    return [];
  });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); });

it("opens own box management in the unified profile and returns to the same wishlist detail", async () => {
  sessionStorage.clear(); history.replaceState({}, "", "/?community=camp&tab=games&wishlist=1");
  const community = { key: "camp", name: "Кэмп", mode: "Camp" as const, timeZoneId: "UTC" };
  discovery = async () => ({ communities: [community], canOpenAdminPanel: false, isSuperAdmin: false });
  const previous = vi.mocked(api).getMockImplementation()!;
  const item = { game: { bggId: 42, name: "Немезида", expansions: [] }, isOwned: true, isWished: true, interestedParticipants: 1, otherInterested: 1, scheduledGatherings: 0, confirmed: false };
  vi.mocked(api).mockImplementation(async (path, options) => {
    const url = new URL(path, "https://test");
    if (path.startsWith("/camp/registration")) return { startDate: "2026-09-26", endDate: "2026-09-27", registration: { registered: true } };
    if (path.startsWith("/catalog")) return { items: [], filters: { types: [], categories: [], providers: [] } };
    if (path.startsWith("/gatherings")) return { items: [] };
    if (path.startsWith("/camp-wishlist")) {
      if (url.searchParams.get("view") === "settings") return { canAct: true, shareCollection: false, shareWishes: false, myDates: ["2026-09-26"], declinedGameIds: [], suggestedGameIds: [] };
      if (url.searchParams.has("view")) return [];
      if (url.searchParams.has("game")) return { item, interested: [], owners: [], requests: [], canAct: true, myDates: ["2026-09-26"] };
      return { items: [item], total: 1, canAct: true, myDates: ["2026-09-26"], updatedAt: "2026-09-24" };
    }
    return previous(path, options);
  });
  await act(async () => root.render(<App />));
  await act(async () => host.querySelector<HTMLButtonElement>(".wish-game-heading")!.click());
  const link = host.querySelector<HTMLAnchorElement>('a[data-profile-nav]')!;
  expect(link.textContent).toBe("Управлять коробкой в профиле");
  await act(async () => link.click());
  expect(link.closest("[hidden]")).not.toBeNull();
  expect(host.querySelector(".camp-privacy")).toBeNull();
  expect(host.querySelector(".profile-collection")?.closest("[hidden]")).toBeNull();
  expect(host.querySelector(".profile-collection")).not.toBeNull();
  const back = [...host.querySelectorAll<HTMLButtonElement>(".page-back")].find(x => !x.closest("[hidden]"))!;
  await act(async () => back.click());
  expect(link.isConnected).toBe(true); expect(link.closest("[hidden]")).toBeNull();
  expect(location.search).toContain("tab=games");
  expect(vi.mocked(api).mock.calls.some(c => c[1]?.method === "POST")).toBe(false);
});

it("keeps the real personal collection available when community discovery fails", async () => {
  await act(async () => root.render(<App />));
  expect(host.textContent).toContain("Моя игра");
  expect(host.textContent).toContain("Доступ к сообществам временно не проверен");
  expect(host.textContent).not.toContain("У вас пока нет доступа ни к одному");
  expect(vi.mocked(api).mock.calls.some(([path]) => path.startsWith("/camp/"))).toBe(false);
  const communities = [...host.querySelectorAll("button")].find(x => x.textContent === "Сообщества")!;
  await act(async () => communities.click());
  expect(host.textContent).toContain("Telegram временно недоступен");
  expect(host.textContent).not.toContain("Моя игра");
});

it("loads the personal collection without waiting for a slow membership check", async () => {
  discovery = () => new Promise(() => {});
  await act(async () => root.render(<App />));
  expect(host.textContent).toContain("Моя игра");
  expect(vi.mocked(api).mock.calls.some(([path]) => path.startsWith("/camp/"))).toBe(false);
});

it("retries discovery without reloading the app and restores verified communities", async () => {
  await act(async () => root.render(<App />));
  discovery = async () => ({ communities: [{ key: "club", name: "Проверенный клуб", mode: "Club", timeZoneId: "UTC" }], canOpenAdminPanel: false, isSuperAdmin: false });
  const retry = [...host.querySelectorAll("button")].find(x => x.textContent === "Повторить проверку")!;
  await act(async () => retry.click());
  expect(host.textContent).toContain("Моя игра");
  expect(host.textContent).not.toContain("Доступ к сообществам временно не проверен");
  expect(vi.mocked(api).mock.calls.filter(([path]) => path === "/communities")).toHaveLength(2);
  expect(vi.mocked(api).mock.calls.filter(([path]) => path === "/capabilities")).toHaveLength(1);
  const communities = [...host.querySelectorAll("button")].find(x => x.textContent === "Сообщества")!;
  await act(async () => communities.click());
  expect(host.textContent).toContain("Проверенный клуб");
});

it("does not open administration when its authorization bootstrap fails", async () => {
  history.replaceState({}, "", "/?admin=1");
  await act(async () => root.render(<App />));
  expect(host.textContent).toContain("Telegram временно недоступен");
  expect(vi.mocked(api).mock.calls.some(([path]) => path.startsWith("/admin/"))).toBe(false);
});
