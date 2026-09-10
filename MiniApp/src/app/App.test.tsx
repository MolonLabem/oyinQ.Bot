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
