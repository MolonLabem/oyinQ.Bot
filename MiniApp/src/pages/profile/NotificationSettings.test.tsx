// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { success: vi.fn() } }));
import { api } from "../../api/client";
import { telegram } from "../../telegram/webApp";
import { NotificationSettings } from "./NotificationSettings";

const settings = { wishlistGathering: true, gatheringFull: false, gatheringDetailsChanged: true, organizerParticipantLeft: true, organizerReplacement: false, organizerBelowMinimum: true, organizerMissingProvider: false, importCompleted: true, reminderLeadMinutes: 60 };
let host: HTMLDivElement; let root: Root;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.clearAllMocks();
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
  vi.mocked(api).mockResolvedValue(settings);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); });
async function submit() { await act(async () => host.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }))); }

it("groups all existing preferences and saves the full edited configuration", async () => {
  await act(async () => root.render(<NotificationSettings />));
  expect([...host.querySelectorAll("legend")].map(x => x.textContent)).toEqual(["Когда вы участвуете", "Когда вы организуете", "Ваша коллекция"]);
  expect(host.querySelectorAll('input[type="checkbox"]')).toHaveLength(8);
  expect(host.querySelector("details")!.open).toBe(false);
  expect(host.textContent).toContain("Важные изменения приходят всегда");
  await act(async () => { host.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[1].click(); const select = host.querySelector("select")!; select.value = "120"; select.dispatchEvent(new Event("change", { bubbles: true })); });
  await submit();
  expect(api).toHaveBeenLastCalledWith("/profile/notifications", expect.objectContaining({ method: "PUT", body: JSON.stringify({ ...settings, gatheringFull: true, reminderLeadMinutes: 120 }) }));
  expect(telegram.success).toHaveBeenCalledOnce();
});

it("retains changes after a failed save and locks controls during a retry", async () => {
  await act(async () => root.render(<NotificationSettings />));
  await act(async () => host.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click());
  vi.mocked(api).mockRejectedValueOnce(new Error("Попробуйте ещё раз")); await submit();
  expect(host.textContent).toContain("Попробуйте ещё раз");
  expect(host.querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked).toBe(false);
  let finish!: () => void; vi.mocked(api).mockReturnValueOnce(new Promise<void>(resolve => { finish = resolve; }));
  await submit(); expect([...host.querySelectorAll("fieldset")].every(x => x.disabled)).toBe(true);
  expect(host.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
  await act(async () => finish()); expect(telegram.success).toHaveBeenCalledOnce();
});
