// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: () => () => {} }, successEventName: "success" }));
import { api } from "../../api/client";
import { GatheringsPage } from "./GatheringsPage";
let root: Root; let host: HTMLDivElement;
const community = { key: "camp", name: "Кэмп", mode: "Camp" as const, timeZoneId: "UTC" };
async function click(text: string) { await act(async () => [...host.querySelectorAll<HTMLButtonElement>("button")].find(b => b.textContent === text)!.click()); }
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.clearAllMocks();
  vi.spyOn(window, "scrollTo").mockImplementation(() => {}); HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.mocked(api).mockResolvedValue({ items: [], hasPrevious: false, hasNext: false });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });
it("requests seats, shows a filtered empty state and omits seats from history", async () => {
  await act(async () => root.render(<GatheringsPage community={community} bggAvailable={false} onInitialConsumed={() => {}} editRegistration={() => {}} openCollection={() => {}} />));
  await click("Есть места");
  expect(api).toHaveBeenLastCalledWith(expect.stringContaining("seats=available"), { cache: "no-store" });
  expect(host.textContent).toContain("Сборов со свободными местами пока нет");
  expect(host.textContent).not.toContain("Создайте первый сбор");
  await click("История");
  expect(host.querySelector('[aria-label="Наличие мест"]')).toBeNull();
  expect(vi.mocked(api).mock.lastCall?.[0]).not.toContain("seats=");
  await click("Отменены"); expect(vi.mocked(api).mock.lastCall?.[0]).toContain("scope=cancelled");
  await click("Предстоящие"); expect(vi.mocked(api).mock.lastCall?.[0]).toContain("seats=available");
  await click("Показать все"); expect(vi.mocked(api).mock.lastCall?.[0]).not.toContain("seats=");
});
it("refreshes the selected filter after mutations and ignores a late response for another filter", async () => {
  await act(async () => root.render(<GatheringsPage community={community} bggAvailable={false} onInitialConsumed={() => {}} editRegistration={() => {}} openCollection={() => {}} />));
  let resolve!: (value: unknown) => void;
  vi.mocked(api).mockImplementationOnce(() => new Promise(r => { resolve = r; }));
  await click("Есть места"); await click("Мест нет");
  await act(async () => resolve({ items: [{ card: { publicId: "old", gameName: "Старый ответ" } }], hasNext: false }));
  expect(host.textContent).not.toContain("Старый ответ");
  await act(async () => window.dispatchEvent(new Event("success")));
  expect(vi.mocked(api).mock.lastCall?.[0]).toContain("seats=full");
});
