// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", () => ({ api: vi.fn() }));
import { api } from "../../api/client";
import { PlayedHistoryDisclosure } from "./PlayedHistory";

let host: HTMLDivElement; let root: Root;
const open = vi.fn();
const item = { gatheringId: "gathering", communityKey: "club", play: { publicId: "play", gameName: "Каркассон", endedAtUtc: "2026-10-23T23:30:00Z", community: "Клуб", timeZoneId: "Asia/Almaty", players: [{ name: "Анна" }] } };
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.clearAllMocks();
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
  vi.mocked(api).mockResolvedValue({ items: [item], hasNext: true });
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); });
async function toggle(value: boolean) { await act(async () => { const details = host.querySelector("details")!; details.open = value; details.dispatchEvent(new Event("toggle")); }); }
async function click(text: string) { await act(async () => { [...host.querySelectorAll("button")].find(button => button.textContent?.includes(text))!.click(); }); }

it("loads on first disclosure, retains the current page on closing, and opens the exact gathering", async () => {
  await act(async () => root.render(<PlayedHistoryDisclosure open={open} />));
  expect(api).not.toHaveBeenCalled();
  await toggle(true); expect(api).toHaveBeenLastCalledWith("/profile/plays?page=1");
  expect(host.textContent).toContain("24.10.2026");
  await click("Далее"); expect(api).toHaveBeenLastCalledWith("/profile/plays?page=2");
  await toggle(false); await toggle(true); expect(api).toHaveBeenCalledTimes(2);
  await click("Каркассон"); expect(open).toHaveBeenCalledWith("club", "gathering");
});

it("retries failed reads and shows a usable empty state", async () => {
  vi.mocked(api).mockRejectedValueOnce(new Error("Не удалось загрузить"));
  await act(async () => root.render(<PlayedHistoryDisclosure open={open} />)); await toggle(true);
  expect(host.textContent).toContain("Не удалось загрузить");
  vi.mocked(api).mockResolvedValue({ items: [], hasNext: false });
  await click("Повторить"); expect(host.textContent).toContain("Подтверждённых партий пока нет");
  expect(host.querySelector("nav")).toBeNull();
});
