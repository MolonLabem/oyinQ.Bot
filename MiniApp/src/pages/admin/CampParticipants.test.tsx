// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { CampAdminParticipants } from "../../api/types";
vi.mock("../../telegram/webApp", () => ({ telegram: { success: vi.fn(), openContact: vi.fn() }, successEventName: "test:success" }));
vi.mock("../../api/client", async original => ({ ...await original<typeof import("../../api/client")>(), api: vi.fn(), download: vi.fn() }));
import { ApiError, api, download } from "../../api/client";
import { telegram } from "../../telegram/webApp";
import { CampParticipants } from "./CampParticipants";

const roster: CampAdminParticipants = {
  campId: 1, campName: "Кэмп у озера", totalCount: 3, availableDates: ["2026-09-01", "2026-09-03"], filterDescription: "Все участники",
  participants: [
    { participantId: 1, displayName: "Аня", city: "Алматы", selectedDates: ["2026-09-01", "2026-09-03"], dayCount: 2, datesText: "01.09.2026, 03.09.2026", needsAccommodation: null, accommodationText: "Не указано", telegramUsername: "anna", contactUrl: "https://t.me/anna?profile" },
    { participantId: 2, displayName: "Борис", city: "Астана", selectedDates: ["2026-09-03"], dayCount: 1, datesText: "03.09.2026", needsAccommodation: false, accommodationText: "Не нужно", contactUrl: "tg://user?id=42" },
    { participantId: 3, displayName: "Игрок с очень длинным именем 🏕️", selectedDates: [], dayCount: 0, datesText: "Не указаны", needsAccommodation: true, accommodationText: "Нужно" },
  ],
};
let host: HTMLDivElement; let root: Root;
let intercept: ((path: string, options?: RequestInit) => Promise<unknown> | undefined) | undefined;
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; }
async function mount(campId = 1) { await act(async () => root.render(<CampParticipants campId={campId} campName={`Кэмп ${campId}`} back={() => {}} />)); }
async function click(text: string) {
  const button = Array.from(host.querySelectorAll("button")).find(item => item.textContent?.trim() === text);
  expect(button, text).toBeDefined(); await act(async () => button!.click());
}
async function change(label: string, value: string) {
  const field = Array.from(host.querySelectorAll("label")).find(item => item.querySelector("span")?.textContent === label)!;
  const control = field.querySelector("input, select") as HTMLInputElement | HTMLSelectElement;
  await act(async () => {
    Object.getOwnPropertyDescriptor(control.tagName === "INPUT" ? HTMLInputElement.prototype : HTMLSelectElement.prototype, "value")!.set!.call(control, value);
    control.dispatchEvent(new Event(control.tagName === "INPUT" ? "input" : "change", { bubbles: true }));
  });
}
const writes = () => vi.mocked(api).mock.calls.filter(([, options]) => options?.method === "POST");
beforeEach(() => {
  vi.clearAllMocks(); vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true); window.scrollTo = vi.fn(); intercept = undefined;
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
  vi.mocked(download).mockResolvedValue();
  vi.mocked(api).mockImplementation(async <T,>(path: string, options?: RequestInit): Promise<T> => {
    const pending = intercept?.(path, options); if (pending) return await pending as T;
    if (options?.method === "POST") return { messageCount: 1, participantCount: 3 } as T;
    const query = new URL(path, "https://example.test").searchParams;
    const participants = roster.participants.filter(person => (!query.get("search") || `${person.displayName} @${person.telegramUsername} ${person.city}`.toLowerCase().includes(query.get("search")!.toLowerCase()))
      && (!query.get("attendanceDate") || person.selectedDates.includes(query.get("attendanceDate")!))
      && (!query.get("accommodation") || person.needsAccommodation === ({ needed: true, "not-needed": false, unanswered: null } as Record<string, boolean | null>)[query.get("accommodation")!]));
    return { ...roster, participants, filterDescription: query.toString() ? "Выбранные условия" : "Все участники" } as T;
  });
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); });

describe("camp participant roster", () => {
  it("shows a real compact table, exact nonconsecutive dates, all housing states and contact actions", async () => {
    await mount();
    expect(host.querySelectorAll("tbody tr")).toHaveLength(3);
    expect(host.textContent).toContain("Показано 3 из 3");
    expect(host.textContent).toContain("Не указано"); expect(host.textContent).toContain("Не нужно");
    expect(host.querySelectorAll("time")).toHaveLength(3);
    expect(host.textContent).toContain("01.09.2026"); expect(host.textContent).not.toContain("02.09.2026");
    await act(async () => host.querySelector<HTMLAnchorElement>('a[href="https://t.me/anna?profile"]')!.click());
    expect(telegram.openContact).toHaveBeenCalledWith("https://t.me/anna?profile");
  });
  it("queries the complete server roster by name, username, city, exact date and accommodation, then resets", async () => {
    await mount();
    await change("Поиск участников", "@anna");
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 300)); });
    expect(api).toHaveBeenLastCalledWith("/admin/camps/1/participants?search=%40anna", undefined);
    expect(host.textContent).toContain("Показано 1 из 3");
    await change("Дата участия", "2026-09-03"); await change("Жильё", "unanswered");
    expect(api).toHaveBeenLastCalledWith("/admin/camps/1/participants?search=%40anna&attendanceDate=2026-09-03&accommodation=unanswered", undefined);
    await click("Сбросить фильтры");
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 300)); });
    expect(host.textContent).toContain("Показано 3 из 3");
  });
  it("exports explicit all or filtered scopes with Excel primary and sends files only through actor-bound routes", async () => {
    await mount(); await change("Жильё", "not-needed");
    await click("Скачать Excel");
    expect(download).toHaveBeenCalledWith("/admin/camps/1/participants/export/xlsx?scope=all", "Участники.xlsx", expect.any(AbortSignal));
    await change("Кого включить", "filtered"); await change("Формат файла", "csv");
    expect(host.textContent).toContain("Выбранные условия · 1 участник");
    await click("Скачать CSV");
    expect(download).toHaveBeenLastCalledWith("/admin/camps/1/participants/export/csv?accommodation=not-needed&scope=filtered", "Участники.csv", expect.any(AbortSignal));
    await click("Отправить файл мне");
    expect(writes()[0]).toEqual(["/admin/camps/1/participants/export/csv/send-to-me?accommodation=not-needed&scope=filtered", expect.objectContaining({ method: "POST", body: undefined })]);
  });
  it("disables synchronous duplicate submissions and never reports success before delivery completes", async () => {
    const pending = deferred<{ participantCount: number }>(); intercept = (_path, options) => options?.method === "POST" ? pending.promise : undefined;
    await mount();
    const button = Array.from(host.querySelectorAll("button")).find(item => item.textContent === "Отправить мне")!;
    await act(async () => { button.click(); button.click(); });
    expect(writes()).toHaveLength(1); expect(telegram.success).not.toHaveBeenCalled();
    expect(Array.from(host.querySelectorAll("button")).filter(item => item.textContent?.includes("Скачать"))[0].disabled).toBe(true);
    await act(async () => pending.resolve({ participantCount: 3 }));
    expect(telegram.success).toHaveBeenCalledWith("Список отправлен: 3 участника");
  });
  it.each(["camp_participant_delivery_unknown", "camp_participant_delivery_partial", undefined])("requires checking Telegram after delivery failure %s and keeps download available", async code => {
    intercept = (_path, options) => options?.method === "POST" ? Promise.reject(new ApiError("Проверьте личный чат", 502, code)) : undefined;
    await mount(); await click("Отправить мне");
    expect(telegram.success).not.toHaveBeenCalled(); expect(host.textContent).toContain("Проверьте личный чат");
    expect(Array.from(host.querySelectorAll("button")).find(item => item.textContent === "Отправить мне")!.disabled).toBe(true);
    await click("Скачать Excel"); expect(download).toHaveBeenCalledTimes(1);
    vi.mocked(download).mockRejectedValueOnce(new ApiError("Не удалось скачать файл", 500));
    await click("Скачать Excel");
    expect(host.textContent).toContain("Не удалось скачать файл");
    expect(Array.from(host.querySelectorAll("button")).find(item => item.textContent === "Отправить мне")!.disabled).toBe(true);
    await click("Проверил личный чат");
    expect(Array.from(host.querySelectorAll("button")).find(item => item.textContent === "Отправить мне")!.disabled).toBe(false);
  });
  it("ignores obsolete filter results and resets the entire screen when the camp changes", async () => {
    const pending = deferred<CampAdminParticipants>();
    intercept = path => path.includes("accommodation=needed") ? pending.promise : undefined;
    await mount(); await change("Жильё", "needed");
    await change("Жильё", "unanswered"); expect(host.querySelectorAll("tbody tr")).toHaveLength(1);
    await act(async () => pending.resolve({ ...roster, participants: [roster.participants[2]] }));
    expect(host.querySelector("tbody")?.textContent).toContain("Аня");
    await mount(2); expect(api).toHaveBeenLastCalledWith("/admin/camps/2/participants", undefined);
    expect((host.querySelector('input[type="search"]') as HTMLInputElement).value).toBe("");
    expect(host.querySelectorAll("tbody tr")).toHaveLength(3);
  });
  it("keeps useful empty, loading and access-revoked states without exposing stale data", async () => {
    await mount();
    intercept = () => Promise.resolve({ ...roster, participants: [], totalCount: 0 }); await mount(2);
    expect(host.textContent).toContain("Пока никто не зарегистрировался");
    intercept = () => Promise.reject(new ApiError("Нет доступа к участникам этого кэмпа.", 403)); await mount(3);
    expect(host.textContent).toContain("Нет доступа"); expect(host.querySelector("table")).toBeNull();
    expect(Array.from(host.querySelectorAll("button")).find(item => item.textContent === "Скачать Excel")!.disabled).toBe(true);
  });
});
