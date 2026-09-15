// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn() }, successEventName: "success" }));
vi.mock("../../components/GamePicker", () => ({ GamePicker: () => null, GameMeta: () => null, searchGames: () => [] }));
import { api } from "../../api/client";
import { ProfileCollectionPage } from "./ProfileCollectionPage";

let host: HTMLDivElement;
let root: Root;
const button = (text: string) => [...host.querySelectorAll("button")].find(x => x.textContent === text)!;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.clearAllMocks(); localStorage.clear();
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
  vi.mocked(api).mockImplementation(async path => {
    if (path.endsWith("/imports/source")) return { source: { bggUsername: "saved-player", lastImportedAt: "2026-09-01T12:00:00Z" } };
    if (path.endsWith("/imports")) return { publicId: "refresh" };
    if (path.includes("/imports/refresh")) return { status: "Failed", stage: "Failed" };
    return [];
  });
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });

it("restores the account from the server without local storage and refreshes without typing", async () => {
  await act(async () => root.render(<ProfileCollectionPage bggAvailable />));
  expect(host.textContent).toContain("saved-player");
  expect(host.textContent).toContain("Последний импорт");
  expect(vi.mocked(api).mock.calls.some(call => call[1]?.method === "POST")).toBe(false);
  await act(async () => button("Обновить из BGG").click());
  const request = vi.mocked(api).mock.calls.find(call => call[0].endsWith("/imports"))!;
  expect(JSON.parse(String(request[1]?.body))).toEqual({});
  await act(async () => button("К моим играм").click());
  expect(host.textContent).toContain("saved-player");
  expect(localStorage.getItem("oyinq-profile-import")).toBeNull();
});

it("prefills account changes and keeps the saved account visible during a provider outage", async () => {
  await act(async () => root.render(<ProfileCollectionPage bggAvailable />));
  await act(async () => button("Сменить аккаунт").click());
  expect(host.querySelector<HTMLInputElement>('input[placeholder="Например, John90"]')?.value).toBe("saved-player");
  await act(async () => button("Отмена").click());
  await act(async () => root.render(<ProfileCollectionPage bggAvailable={false} />));
  expect(host.textContent).toContain("saved-player");
  expect(button("Обновить из BGG").disabled).toBe(true);
});
