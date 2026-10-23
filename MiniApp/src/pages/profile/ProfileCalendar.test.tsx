// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { ProfileGathering } from "../../api/types";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn(), download: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
import { api, download } from "../../api/client";
import { ProfileCalendar } from "./ProfileCalendar";

const gathering = (id: string, date: string, utc: string, overrides: Partial<ProfileGathering> = {}): ProfileGathering => ({
  publicId: id, communityKey: "camp", communityName: "Кэмп", communityMode: "Camp", gameName: id,
  localDate: date, localTime: "12:00", localDateTime: `${date}, 12:00`, startsAtUtc: utc, isOrganizer: false, ...overrides
});
let host: HTMLDivElement; let root: Root; let items: ProfileGathering[];
const open = vi.fn();
beforeEach(() => {
  vi.clearAllMocks(); vi.useFakeTimers({ toFake: ["Date"] }); vi.setSystemTime(new Date("2026-10-23T12:00:00Z"));
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); sessionStorage.clear();
  items = [
    gathering("Новая Зеландия", "2026-10-31", "2026-10-30T13:00:00Z", { communityKey: "nz", communityName: "Клуб NZ", localTime: "02:00" }),
    gathering("Бункер", "2026-10-31", "2026-10-31T07:00:00Z", { isOrganizer: true, progress: "started" }),
    gathering("Нью-Йорк", "2026-10-31", "2026-11-01T01:00:00Z", { communityKey: "ny", communityName: "Клуб NY", localTime: "21:00", waitlistPosition: 2 }),
    gathering("Ноябрьская игра", "2026-11-01", "2026-11-01T07:00:00Z", { progress: "awaitingOutcome" })
  ];
  vi.mocked(api).mockImplementation(async path => path === "/profile/gatherings" ? [...items] : { items: [], hasMore: false });
  vi.mocked(download).mockResolvedValue(undefined);
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); vi.useRealTimers(); });
async function mount(contextKey = "global") {
  await act(async () => root.render(<ProfileCalendar contextKey={contextKey} timeZoneId="Asia/Almaty" communities={[]} open={open} />));
}
const day = (date: string) => host.querySelector<HTMLButtonElement>(`[data-calendar-date="${date}"]`)!;
const action = (label: string) => host.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
const selected = () => host.querySelector('td[aria-selected="true"] button')!.getAttribute("data-calendar-date");
async function click(element: HTMLElement) { await act(async () => element.click()); }
async function key(value: string) { await act(async () => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: value, bubbles: true }))); }

it("marks community-local dates and shows just the selected day's chronological schedule with exact navigation", async () => {
  await mount();
  expect(api).toHaveBeenCalledWith("/profile/gatherings", { cache: "no-store" });
  expect(selected()).toBe("2026-10-31"); expect(day("2026-10-31").getAttribute("aria-label")).toContain("3 сбора");
  expect(host.querySelectorAll(".profile-schedule-item")).toHaveLength(3);
  expect([...host.querySelectorAll(".schedule-time")].map(x => x.textContent)).toEqual(["02:00", "12:00", "21:00"]);
  expect(host.textContent).toContain("Вы организатор"); expect(host.textContent).toContain("Сбор начался");
  expect(host.textContent).toContain("Лист ожидания · позиция 2");
  expect(host.textContent).not.toContain("Ноябрьская игра");
  await click(host.querySelectorAll<HTMLButtonElement>(".profile-schedule-item")[2]);
  expect(open).toHaveBeenCalledWith("ny", "Нью-Йорк");
  expect(vi.mocked(api).mock.calls.some(([, options]) => options?.method)).toBe(false);
});

it("navigates months, empty days, Today and the nearest gathering without writing data", async () => {
  await mount(); await click(action("Следующий месяц")); expect(selected()).toBe("2026-11-01");
  expect(host.textContent).toContain("Ожидается подтверждение исхода");
  await click(day("2026-11-02")); expect(host.textContent).toContain("На этот день сборов нет");
  await click([...host.querySelectorAll<HTMLButtonElement>("button")].find(x => x.textContent === "К ближайшему сбору")!);
  expect(selected()).toBe("2026-10-31");
  await click([...host.querySelectorAll<HTMLButtonElement>("button")].find(x => x.textContent === "Сегодня")!);
  expect(selected()).toBe("2026-10-23"); expect(day("2026-10-23").getAttribute("aria-current")).toBe("date");
  await click(action("Предыдущий месяц")); expect(selected()).toBe("2026-09-01");
  expect(vi.mocked(api).mock.calls.some(([, options]) => options?.method)).toBe(false);
});

it("supports keyboard focus across leap days, month boundaries and year boundaries", async () => {
  sessionStorage.setItem("oyinq-profile-calendar:global", "2024-02-29"); await mount();
  day("2024-02-29").focus(); await key("PageDown"); expect(selected()).toBe("2024-03-29");
  await key("PageUp"); expect(selected()).toBe("2024-02-29");
  await key("ArrowRight"); expect(selected()).toBe("2024-03-01");
  expect(document.activeElement).toBe(day("2024-03-01"));
  await key("Home"); expect(selected()).toBe("2024-02-26");
  await key("End"); expect(selected()).toBe("2024-03-03");
  await act(async () => root.render(null)); sessionStorage.setItem("oyinq-profile-calendar:global", "2026-12-31"); await mount();
  day("2026-12-31").focus(); await key("ArrowRight"); expect(selected()).toBe("2027-01-01");
});

it("preserves the chosen date after refresh and remount without sharing another context's selection", async () => {
  await mount(); await click(action("Следующий месяц"));
  items = items.filter(item => item.localDate === "2026-11-01");
  await act(async () => window.dispatchEvent(new Event("focus")));
  expect(selected()).toBe("2026-11-01");
  await act(async () => root.render(null)); await mount(); expect(selected()).toBe("2026-11-01");
  items = [gathering("Декабрь", "2026-12-12", "2026-12-12T07:00:00Z")];
  await act(async () => root.render(null)); await mount("other"); expect(selected()).toBe("2026-12-12");
});

it("retries a failed read and retains selection while calendar export fails and is retried", async () => {
  vi.mocked(api).mockRejectedValueOnce(new Error("Не удалось загрузить расписание")); await mount();
  expect(host.textContent).toContain("Не удалось загрузить расписание");
  await click([...host.querySelectorAll<HTMLButtonElement>("button")].find(x => x.textContent === "Повторить")!);
  expect(selected()).toBe("2026-10-31");
  vi.mocked(download).mockRejectedValueOnce(new Error("Скачивание недоступно")); await click(action("Скачать календарь"));
  expect(host.textContent).toContain("Скачивание недоступно"); expect(action("Скачать календарь").disabled).toBe(false);
  await click(action("Скачать календарь"));
  expect(download).toHaveBeenLastCalledWith("/profile/gatherings.ics", "oyinq-agenda.ics");
  expect(host.textContent).not.toContain("Скачивание недоступно"); expect(selected()).toBe("2026-10-31");
});

it("keeps an empty calendar usable with no community, no events and an invalid saved date", async () => {
  items = []; sessionStorage.setItem("oyinq-profile-calendar:global", "2026-02-31"); await mount();
  expect(selected()).toBe("2026-10-23"); expect(host.textContent).toContain("не записаны");
  expect(host.querySelector(".calendar-attention")).toBeNull();
  await click(action("Следующий месяц")); expect(selected()).toBe("2026-11-01");
});
