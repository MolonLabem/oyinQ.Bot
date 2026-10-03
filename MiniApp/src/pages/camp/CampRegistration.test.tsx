// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { CampRegistrationSettings } from "./CampRegistration";
import { api } from "../../api/client";
import type { CampConfiguration } from "../../api/types";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { success: vi.fn(), confirm: vi.fn() } }));
const config: CampConfiguration = { version: 1, description: "🎃 Программа\nИгры до полуночи", locationName: "Дом", locationUrl: "https://example.test", pricing: { currency: "KZT", amount: 7500, perDay: false }, registrationFields: [
  { id: "costume", label: "Будете в костюме?", type: "Choice", required: true, options: [{ id: "yes", label: "Да", amount: 0 }, { id: "no", label: "Нет", amount: 1000 }], amount: 0, perDay: false }
] };
let host: HTMLDivElement; let root: Root;
const community = { key: "camp", name: "Хэллоуин", mode: "Camp" as const, timeZoneId: "UTC" };
const state = () => ({ configuration: config, campStatus: "Active", startDate: "2026-10-31", endDate: "2026-11-01", availableDates: ["2026-10-31", "2026-11-01"], baseGameIds: [], displayName: "Игрок", registration: { registered: true, selectedDates: ["2026-10-31"], city: "Алматы", needsAccommodation: false, data: { answers: {} } } });
const quote = (total: number) => ({ currency: "KZT", total, lines: [{ label: "Участие", quantity: 1, unitAmount: total, amount: total }], calculatedAtUtc: "2026-10-26T00:00:00Z" });
beforeEach(() => { vi.clearAllMocks(); vi.useFakeTimers(); Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.stubGlobal("scrollTo", vi.fn()); host = document.createElement("div"); document.body.append(host); root = createRoot(host); vi.mocked(api).mockImplementation(async path => path.includes("/quote") ? { quote: quote(7500) } : state()); });
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.useRealTimers(); vi.unstubAllGlobals(); });
async function mount() { await act(async () => root.render(<CampRegistrationSettings community={community} />)); }
async function choose(value: string) { await act(async () => { const select = host.querySelector<HTMLSelectElement>("select")!; select.value = value; select.dispatchEvent(new Event("change", { bubbles: true })); }); }
const save = () => [...host.querySelectorAll("button")].find(button => button.textContent === "Сохранить изменения")!;
it("shows details, requires custom answers and sends choices without a client total", async () => {
  await mount(); expect(host.textContent).toContain("Игры до полуночи");
  await act(async () => save().click()); expect(host.textContent).toContain("Заполните обязательный вопрос");
  expect(vi.mocked(api).mock.calls.some(([, options]) => options?.method === "PUT")).toBe(false);
  await choose("no"); await act(async () => save().click());
  const request = vi.mocked(api).mock.calls.find(([, options]) => options?.method === "PUT")!;
  const body = JSON.parse(String(request[1]?.body)); expect(body.answers).toEqual({ costume: "no" }); expect(body).not.toHaveProperty("total");
});
it("hides stale quotes during a choice change and ignores late responses", async () => {
  let resolveOld!: (value: unknown) => void;
  const old = new Promise(done => { resolveOld = done; });
  vi.mocked(api).mockImplementation(async (path, options) => path.includes("/quote") ? JSON.parse(String(options?.body)).answers.costume === "no" ? { quote: quote(8500) } : await old : state());
  await mount(); await choose("no"); expect(host.textContent).toContain("Обновляем расчёт");
  await act(async () => { await vi.advanceTimersByTimeAsync(250); });
  expect(host.querySelector(".camp-quote-total")?.textContent).toContain("8 500");
  await act(async () => resolveOld({ quote: quote(1) }));
  expect(host.querySelector(".camp-quote-total")?.textContent).toContain("8 500");
});
