// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn(), confirm: vi.fn(async () => true), success: vi.fn(() => window.dispatchEvent(new Event("success"))) }, successEventName: "success" }));
vi.mock("../../components/GamePicker", () => ({
  GameMeta: () => null, searchGames: (games: unknown[]) => games,
  GamePicker: ({ onSelect }: { onSelect: (game: object) => void }) => <button onClick={() => onSelect({ bggId: 88, name: "Новая игра", expansions: [] })}>Выбрать новую игру</button>
}));
import { api } from "../../api/client";
import { CampPrivacy, CampProfileProvider } from "./CampProfileContext";
import { ProfileCollectionPage, CampRegistrationGate } from "./ProfileCollectionPage";
import { ProfilePage } from "./ProfilePage";
import type { Contribution } from "../../api/types";

const camp = { key: "camp", name: "Очень длинное название осеннего кэмпа", mode: "Camp" as const, timeZoneId: "UTC" };
const item = { bggId: 42, itemType: "BaseGame" as const, snapshot: { name: "Немезида" } };
let host: HTMLDivElement; let root: Root; let contributions: Contribution[];
let settings: { canAct: boolean; shareCollection: boolean; shareWishes: boolean; myDates: string[]; declinedGameIds: number[]; suggestedGameIds: number[] };
const visible = (selector: string) => [...host.querySelectorAll<HTMLElement>(selector)].filter(x => !x.closest("[hidden]") && ![...x.parentElement?.closest("details:not([open])")?.querySelectorAll("button") ?? []].includes(x as HTMLButtonElement));
async function click(text: string) { const b = [...host.querySelectorAll<HTMLButtonElement>("button")].find(x => x.textContent === text)!; expect(b).toBeTruthy(); await act(async () => b.click()); }
async function mount(privacy = false) { await act(async () => root.render(<CampProfileProvider key={camp.key} community={camp}>{privacy && <CampPrivacy community={camp} />}<ProfileCollectionPage community={camp} bggAvailable /></CampProfileProvider>)); }
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.clearAllMocks(); sessionStorage.clear(); localStorage.clear();
  vi.spyOn(window, "scrollTo").mockImplementation(() => {}); HTMLElement.prototype.scrollIntoView = vi.fn();
  contributions = []; settings = { canAct: true, shareCollection: true, shareWishes: true, myDates: ["2026-09-26", "2026-09-27"], declinedGameIds: [], suggestedGameIds: [42] };
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (init?.method && init.method !== "GET") return {};
    if (path.includes("view=settings")) return { ...settings };
    if (path.includes("view=incoming")) return [];
    if (path.startsWith("/camp/contributions")) return contributions;
    if (path.endsWith("/imports/source")) return { source: null };
    if (path === "/profile/collection/") return [item];
    return [];
  });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });

it("keeps the collection free of visibility controls and addition closed without losing the selected game", async () => {
  await mount();
  expect(host.querySelector("#collection-add")?.hasAttribute("hidden")).toBe(true);
  expect(visible(".collection-item")).toHaveLength(1);
  expect(host.querySelector(".camp-privacy")).toBeNull();
  await click("Добавить ▾"); await click("Выбрать новую игру"); await click("Свернуть добавление ▴");
  expect(host.textContent).toContain("Выбрана игра «Новая игра»");
  await click("Продолжить добавление");
  expect(host.querySelector("#collection-add")?.hasAttribute("hidden")).toBe(false);
  expect(host.querySelector(".selected-game")?.textContent).toContain("Новая игра");
  expect(vi.mocked(api).mock.calls.filter(c => c[1]?.method)).toHaveLength(0);
});

it("confirms a new box for attendance days without a date dialog", async () => {
  await mount(); const select = host.querySelector<HTMLSelectElement>('.box-control select')!;
  await act(async () => { select.value = "Bringing"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  const request = vi.mocked(api).mock.calls.find(c => c[0].includes("/commitment"))!;
  expect(JSON.parse(String(request[1]?.body))).toEqual({ communityKey: "camp", commitment: "Bringing", allAttendanceDays: true });
  expect(host.querySelector(".wish-dates")).toBeNull();
});

it("preserves explicit dates even when they coincide with every attendance day", async () => {
  contributions = [{ ...item, source: "Manual", commitment: "Available", allAttendanceDays: false, selectedDates: settings.myDates, availableDates: settings.myDates }];
  await mount(); expect(host.textContent).toContain("Только 26 сентября — 27 сентября");
  const select = host.querySelector<HTMLSelectElement>('.box-control select')!;
  await act(async () => { select.value = "Bringing"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  const request = vi.mocked(api).mock.calls.find(c => c[0].includes("/commitment"))!;
  expect(JSON.parse(String(request[1]?.body))).toEqual({ communityKey: "camp", commitment: "Bringing" });
});

it("does not show privacy as saved when the server rejects it", async () => {
  const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => { if (init?.method === "POST") throw new Error("Не удалось сохранить видимость"); return previous(path, init); });
  await mount(true); const checkbox = host.querySelector<HTMLInputElement>('.camp-privacy input')!;
  await act(async () => checkbox.click());
  expect(checkbox.checked).toBe(false); expect(host.textContent).toContain("Не удалось сохранить видимость");
  expect(vi.mocked(api).mock.calls.some(c => c[0].includes("/commitment"))).toBe(false);
});

it("shows optional hiding only in Camp settings and saves each server choice separately", async () => {
  const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (init?.method === "POST") {
      const { action, ...change } = JSON.parse(String(init.body)); expect(action).toBe("privacy");
      Object.assign(settings, change); return {};
    }
    if (path === "/profile") return { telegramDisplayName: "Участник", botStartRequired: false };
    if (path === "/profile/notifications") return { reminderLeadMinutes: 0 };
    if (path.startsWith("/camp/registration")) return { campStatus: "Active", startDate: "2026-09-26", endDate: "2026-09-27", availableDates: settings.myDates };
    if (path.startsWith("/camp-wishlist") && !path.includes("view=")) return { ...settings, items: [], total: 0, suggestions: 0, updatedAt: "2026-09-24T10:00:00Z" };
    return previous(path, init);
  });
  await act(async () => root.render(<ProfilePage community={camp} communities={[camp]} bggAvailable openGathering={vi.fn()} />));
  expect(host.querySelector(".camp-privacy")).toBeNull();
  await click("Хотелки"); expect(host.querySelector(".camp-privacy")).toBeNull();
  expect(host.querySelector('[href*="camp-privacy"]')).toBeNull();
  await click("Настройки");
  const choices = host.querySelectorAll<HTMLInputElement>('.camp-privacy input[type="checkbox"]');
  expect(choices).toHaveLength(2); expect([...choices].every(x => !x.checked)).toBe(true);
  expect(host.querySelector(".camp-privacy")?.textContent).toContain("Видимость в этом кэмпе");
  await act(async () => choices[0].click());
  expect(choices[0].checked).toBe(true); expect(choices[1].checked).toBe(false);
  expect(settings.shareCollection).toBe(false); expect(settings.shareWishes).toBe(true);
  await act(async () => choices[1].click());
  expect(settings.shareWishes).toBe(false);
  expect(vi.mocked(api).mock.calls.filter(c => c[1]?.method).map(c => JSON.parse(String(c[1]?.body)))).toEqual([
    { action: "privacy", shareCollection: false }, { action: "privacy", shareWishes: false }
  ]);
  await click("Игры"); expect(host.querySelector(".camp-privacy")).toBeNull();
});

it("explains Camp visibility during registration without another consent checkbox", async () => {
  const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => path.startsWith("/camp/registration")
    ? { campStatus: "Active", startDate: "2026-09-26", endDate: "2026-09-27", availableDates: settings.myDates, shareCollection: true, shareWishes: true }
    : previous(path, init));
  await act(async () => root.render(<CampRegistrationGate community={camp} canOpenAdminPanel={false}>Готово</CampRegistrationGate>));
  expect(host.textContent).toContain("Вашу коллекцию и хотелки видят участники этого кэмпа. Это можно изменить в настройках.");
  // Two dates and accommodation are the existing registration inputs.
  expect(host.querySelectorAll('input[type="checkbox"]')).toHaveLength(3);
  expect(vi.mocked(api).mock.calls.some(c => c[1]?.method)).toBe(false);
});

it("keeps declined distinct from withdrawing an offer and puts ownership deletion in the menu", async () => {
  settings.declinedGameIds = [42]; await mount();
  const select = host.querySelector<HTMLSelectElement>('.box-control select')!; expect(select.value).toBe("declined");
  expect(host.querySelector('.collection-menu')?.hasAttribute("open")).toBe(false);
  await act(async () => { select.value = ""; select.dispatchEvent(new Event("change", { bubbles: true })); });
  const request = vi.mocked(api).mock.calls.find(c => c[1]?.method === "POST")!;
  expect(JSON.parse(String(request[1]?.body))).toEqual({ action: "withdraw", bggId: 42 });
  expect(vi.mocked(api).mock.calls.some(c => c[1]?.method === "DELETE")).toBe(false);
});

it("retains the running import and a visible resume action when closed", async () => {
  localStorage.setItem("oyinq-profile-import", "job"); const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => path.includes("/imports/job") ? { status: "Running", stage: "Queued" } : previous(path, init));
  await mount(); await click("Продолжить позже");
  expect(localStorage.getItem("oyinq-profile-import")).toBe("job");
  expect(visible('[role="status"]')[0].textContent).toContain("Импорт BGG: выполняется");
  await click("Открыть"); expect(visible("h1").some(x => x.textContent === "Импорт BGG")).toBe(true);
  expect(vi.mocked(api).mock.calls.some(c => c[1]?.method === "POST")).toBe(false);
});
