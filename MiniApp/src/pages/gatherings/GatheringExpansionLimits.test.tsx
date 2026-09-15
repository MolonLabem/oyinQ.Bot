// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { Community, GatheringDetail } from "../../api/types";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn(), gatheringMutation: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { success: vi.fn() }, successEventName: "success" }));
vi.mock("../../components/Wishlist", () => ({ WishButton: () => null }));
import { api } from "../../api/client";
import { CreateGathering, EditGathering } from "./GatheringsPage";

const expansion = { bggId: 315895, name: "Prophecy of Kings", minPlayers: 3, maxPlayers: 8 };
const game = { bggId: 233078, name: "Twilight Imperium", minPlayers: 3, maxPlayers: 6, bestPlayers: "6", expansions: [expansion] };
let host: HTMLDivElement; let root: Root;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.clearAllMocks();
  vi.useFakeTimers({ toFake: ["Date"] }); vi.setSystemTime(new Date("2026-09-10T12:00:00Z"));
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  vi.mocked(api).mockImplementation(async path => path.startsWith("/games?") ? [game] : { providers: [], isConfirmed: false, summary: "Нет коробки" });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); vi.useRealTimers(); });

it.each(["Club", "Camp"] as const)("updates both targets in %s creation and editing", async mode => {
  const community: Community = { key: mode, name: mode, mode, timeZoneId: "UTC", startsAtUtc: "2026-09-11T00:00:00Z", endsAtUtc: "2026-09-15T00:00:00Z" };
  const values = () => [...host.querySelectorAll<HTMLSelectElement>(".limits select")].map(x => x.value);
  const toggle = () => [...host.querySelectorAll("label")].find(x => x.textContent?.includes("Prophecy of Kings"))!.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
  await act(async () => root.render(<CreateGathering community={community} initialGameId={233078} bggAvailable={false} onDone={() => {}} editRegistration={() => {}} />));
  expect(values()).toEqual(["3", "6", "6"]);
  await act(async () => toggle().click()); expect(values()).toEqual(["3", "8", "8"]);
  const detail = { startsAtLocal: "2026-09-12T12:00", minimumPlayers: 3, desiredPlayers: 8, maximumPlayers: 8,
    gameBaseMinimumPlayers: 3, gameBaseMaximumPlayers: 6, gameMinimumPlayers: 3, gameMaximumPlayers: 8,
    selectedExpansionIds: [315895], knownExpansions: [expansion], canTeachRules: true } as GatheringDetail;
  await act(async () => root.render(<EditGathering community={community} id="saved" value={detail} done={() => {}} cancel={() => {}} />));
  expect(values()).toEqual(["3", "8", "8"]);
  await act(async () => toggle().click()); expect(values()).toEqual(["3", "6", "6"]);
  await act(async () => toggle().click()); expect(values()).toEqual(["3", "8", "8"]);
});
