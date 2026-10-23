// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { Community } from "../../api/types";

const nativeBack = vi.hoisted(() => ({ show: vi.fn(), hide: vi.fn(), onClick: vi.fn(), offClick: vi.fn() }));
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", async () => {
  const { backButtonStack } = await import("../../telegram/backButtonStack");
  return { telegram: { hasBackButton: true, back: backButtonStack(nativeBack), success: vi.fn(), confirm: vi.fn() }, successEventName: "success" };
});
import { api } from "../../api/client";
import { CampProfileProvider } from "./CampProfileContext";
import { ProfileSettings, type ProfileSettingSection } from "./ProfileSettings";
import { ProfilePage } from "./ProfilePage";

const camp: Community = { key: "camp", name: "Осенний кэмп", mode: "Camp", timeZoneId: "UTC" };
const profile = { telegramDisplayName: "Имя Telegram", preferredDisplayName: "Игрок", botStartRequired: false };
const registration = { campStatus: "Active", startDate: "2026-10-24", endDate: "2026-10-25", availableDates: ["2026-10-24", "2026-10-25"],
  registration: { registered: true, selectedDates: ["2026-10-24"], needsAccommodation: false, city: "Алматы" } };
let host: HTMLDivElement; let root: Root;
function Settings({ community }: { community?: Community }) {
  const [section, open] = useState<ProfileSettingSection>();
  return <ProfileSettings community={community} profile={profile} reloadProfile={async () => true} section={section} open={open} />;
}
beforeEach(() => {
  vi.clearAllMocks(); Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  sessionStorage.clear(); localStorage.clear(); history.replaceState({}, "", "/");
  vi.spyOn(window, "scrollTo").mockImplementation(() => {}); HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.mocked(api).mockImplementation(async path => {
    if (path === "/profile") return profile;
    if (path.startsWith("/camp/registration")) return registration;
    if (path.includes("view=settings")) return { canAct: true, shareCollection: false, shareWishes: true, myDates: ["2026-10-24"], declinedGameIds: [], suggestedGameIds: [] };
    if (path === "/profile/notifications") return { reminderLeadMinutes: 60 };
    return [];
  });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });
async function openSetting(name: ProfileSettingSection) {
  await act(async () => host.querySelector<HTMLButtonElement>(`[data-setting="${name}"]`)!.click());
}
async function back() { await act(async () => nativeBack.onClick.mock.calls.at(-1)![0]()); }

it("shows current values in a short overview and loads only the selected editor", async () => {
  await act(async () => root.render(<CampProfileProvider community={camp}><Settings community={camp} /></CampProfileProvider>));
  expect(host.querySelectorAll(".settings-row")).toHaveLength(5);
  expect(host.querySelector('[data-setting="registration"]')!.textContent).toContain("24 октября · без жилья");
  expect(host.querySelector('[data-setting="privacy"]')!.textContent).toContain("Коллекция скрыта");
  expect(host.querySelector("form")).toBeNull(); expect(host.querySelector("input")).toBeNull();
  expect(api).not.toHaveBeenCalledWith("/profile/notifications");
  await openSetting("notifications");
  expect(api).toHaveBeenCalledWith("/profile/notifications");
  expect(host.querySelector(".camp-registration-form")).toBeNull();
  await back(); expect(host.querySelector(".profile-settings-overview")).not.toBeNull();
  expect(document.activeElement).toBe(host.querySelector('[data-setting="notifications"]'));
  await openSetting("registration");
  expect(host.querySelector(".camp-registration-form")).not.toBeNull();
  expect(host.querySelector(".camp-privacy")).toBeNull();
  await back(); expect(host.querySelector(".camp-registration-form")).toBeNull();
  expect(vi.mocked(api).mock.calls.every(([, options]) => !options?.method || options.method === "GET")).toBe(true);
});

it("keeps global settings available without requesting Camp data", async () => {
  await act(async () => root.render(<Settings />));
  expect(host.querySelectorAll(".settings-row")).toHaveLength(3);
  expect(vi.mocked(api).mock.calls).toHaveLength(0);
  await openSetting("identity");
  expect(host.querySelector<HTMLInputElement>('input')!.value).toBe("Игрок");
  await back(); await openSetting("deletion");
  expect(host.querySelector(".danger-zone")!.textContent).toContain("Удалить мой профиль");
  expect(vi.mocked(api).mock.calls).toHaveLength(0);
});

it("refreshes the registration summary and shared Camp state after saving new attendance days", async () => {
  const previous = vi.mocked(api).getMockImplementation()!;
  let saved = false;
  vi.mocked(api).mockImplementation(async (path, options) => {
    if (path === "/camp/registration" && options?.method === "PUT") { saved = true; return {}; }
    if (saved && path.startsWith("/camp/registration")) return { ...registration, registration: { ...registration.registration, selectedDates: registration.availableDates } };
    return previous(path, options);
  });
  await act(async () => root.render(<CampProfileProvider community={camp}><Settings community={camp} /></CampProfileProvider>));
  await openSetting("registration");
  await act(async () => host.querySelectorAll<HTMLInputElement>(".registration-day input")[1].click());
  await act(async () => host.querySelector<HTMLButtonElement>('button[type="submit"]')!.click());
  expect(host.querySelector(".profile-settings-overview")).not.toBeNull();
  expect(host.querySelector('[data-setting="registration"]')!.textContent).toContain("24 октября — 25 октября");
  expect(vi.mocked(api).mock.calls.filter(([path]) => path.includes("view=settings"))).toHaveLength(2);
  expect(JSON.parse(String(vi.mocked(api).mock.calls.find(([, options]) => options?.method === "PUT")![1]!.body)).selectedDates).toEqual(registration.availableDates);
});

it("opens registration directly from an edit request and returns to Settings with Telegram Back", async () => {
  await act(async () => root.render(<ProfilePage community={camp} communities={[camp]} bggAvailable={false}
    editRequest={1} onEditRequestConsumed={vi.fn()} openGathering={vi.fn()} />));
  expect(host.querySelector(".camp-registration-form")).not.toBeNull();
  expect(host.querySelector('[aria-selected="true"]')!.textContent).toBe("Настройки");
  await back();
  expect(host.querySelector(".profile-settings-overview")).not.toBeNull();
  expect(host.querySelector(".camp-registration-form")).toBeNull();
});
