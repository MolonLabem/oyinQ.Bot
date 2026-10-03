// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { CampConfiguration, CampRegistrationQuote } from "../api/types";
vi.mock("../api/client", async original => ({ ...await original<typeof import("../api/client")>(), api: vi.fn() }));
import { api } from "../api/client";
import { CampPricingPreview } from "./CampPricingPreview";

let host: HTMLDivElement; let root: Root;
const configuration: CampConfiguration = { version: 1, pricing: { currency: "KZT", amount: 10000, perDay: true, accommodationAmount: 500, accommodationPerDay: true },
  registrationFields: [{ id: "meal", label: "Ужин", type: "Choice", required: true, amount: 0, perDay: true,
    options: [{ id: "no", label: "Без ужина", amount: 0 }, { id: "yes", label: "С ужином", amount: 1500 }] }] };
const quote = (total: number): CampRegistrationQuote => ({ currency: "KZT", total, lines: [{ label: "Участие", quantity: 1, unitAmount: total, amount: total }], calculatedAtUtc: "2026-10-03T00:00:00Z" });
beforeEach(() => {
  vi.clearAllMocks(); vi.useFakeTimers(); Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.useRealTimers(); });

it("previews priced choices using server totals and hides obsolete results after edits", async () => {
  let finishOld!: (value: { quote: CampRegistrationQuote }) => void;
  vi.mocked(api).mockReturnValueOnce(new Promise(resolve => { finishOld = resolve; })).mockResolvedValue({ quote: quote(23000) });
  await act(async () => root.render(<form><CampPricingPreview configuration={configuration} timeZoneId="Asia/Qyzylorda" /></form>));
  expect(host.textContent).toContain("Участники увидят");
  const days = host.querySelector<HTMLSelectElement>("select")!;
  const meal = host.querySelectorAll<HTMLSelectElement>("select")[1];
  expect(meal.required).toBe(false);
  await act(async () => { days.value = "2"; days.dispatchEvent(new Event("change", { bubbles: true })); meal.value = "yes"; meal.dispatchEvent(new Event("change", { bubbles: true })); });
  expect(host.textContent).not.toContain("Пример итоговой суммы");
  await act(async () => vi.advanceTimersByTimeAsync(250));
  const request = JSON.parse(String(vi.mocked(api).mock.calls.at(-1)![1]?.body));
  expect(request).toMatchObject({ days: 2, answers: { meal: "yes" }, timeZoneId: "Asia/Qyzylorda", needsAccommodation: false });
  expect(request).not.toHaveProperty("total");
  expect(host.querySelector(".camp-quote-total dd")!.textContent).toContain("23");
  await act(async () => finishOld({ quote: quote(1) }));
  expect(host.querySelector(".camp-quote-total dd")!.textContent).toContain("23");
  const housing = host.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
  await act(async () => housing.click());
  await act(async () => vi.advanceTimersByTimeAsync(250));
  expect(JSON.parse(String(vi.mocked(api).mock.calls.at(-1)![1]?.body)).needsAccommodation).toBe(true);
});
