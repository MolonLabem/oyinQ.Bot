// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { GatheringDetail } from "../../api/types";

const mocks = vi.hoisted(() => ({ api: vi.fn(), mutation: vi.fn(), success: vi.fn(), back: vi.fn() }));
vi.mock("../../api/client", () => ({ api: mocks.api, gatheringMutation: mocks.mutation,
  json: (method: string, body: unknown) => ({ method, body: JSON.stringify(body) }) }));
vi.mock("../../telegram/webApp", () => ({ telegram: { success: mocks.success, back: mocks.back }, successEventName: "success" }));
vi.mock("./useGatheringExpansions", () => ({ useGatheringExpansions: () => ({ expansions: [], notice: null }) }));
import { ParticipantNameEditor } from "./ParticipantNameEditor";
import { EditGathering } from "./GatheringsPage";
import { PlayPanel } from "./PlayPanel";

let root: Root; let container: HTMLDivElement;
beforeEach(() => {
  vi.clearAllMocks();
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  mocks.mutation.mockResolvedValue(undefined);
});
afterEach(async () => { await act(async () => root.unmount()); container.remove(); });
async function click(label: string) {
  const button = [...container.querySelectorAll("button")].find(x => x.textContent === label || x.getAttribute("aria-label") === label);
  expect(button, label).toBeDefined(); await act(async () => button!.click());
}
async function input(value: string) {
  const field = container.querySelector<HTMLInputElement>('.participant-name-settings input')!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(field, value);
    field.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

describe("participant names inside one gathering", () => {
  it("edits only the chosen name, trims it, returns to roster and clears to current profile name", async () => {
    const changed = vi.fn();
    function Harness() {
      const [name, setName] = useState<string | null>(null);
      return <><ParticipantNameEditor originalName="Виктор" displayNameOverride={name} onChange={x => { changed(x); setName(x); }} />
        <ParticipantNameEditor originalName="Антон" onChange={changed} /></>;
    }
    await act(async () => root.render(<Harness />));
    expect(container.querySelector("input")).toBeNull();
    await click("Виктор"); expect(container.textContent).toContain("Имя в этой партии: Виктор");
    expect(container.textContent).not.toContain("Использовать исходное имя");
    await click("✏️ Изменить имя"); await input("  Виктор + жена  "); await click("Применить имя");
    expect(changed).toHaveBeenCalledExactlyOnceWith("Виктор + жена");
    expect(container.querySelector("input")).toBeNull(); expect(container.textContent).toContain("Антон");
    await click("Виктор + жена"); await click("↩️ Использовать исходное имя");
    expect(changed).toHaveBeenLastCalledWith(null); expect(container.textContent).toContain("Виктор");
  });

  it("dismisses an unfinished edit without changing the confirmed name", async () => {
    const changed = vi.fn();
    await act(async () => root.render(<ParticipantNameEditor originalName="Виктор" onChange={changed} />));
    await click("Виктор"); await click("✏️ Изменить имя"); await input("Черновик"); await click("Назад к составу");
    expect(changed).not.toHaveBeenCalled(); expect(container.querySelector("input")).toBeNull();
    await click("Виктор"); await click("✏️ Изменить имя");
    expect(container.querySelector<HTMLInputElement>("input")!.value).toBe("Виктор");
    await input("   "); await click("Применить имя"); expect(changed).toHaveBeenCalledWith(null);
  });

  it("saves only edited participant ids alongside the gathering without restarting its form", async () => {
    const done = vi.fn();
    const value = { startsAtLocal: "2070-01-01T18:00", minimumPlayers: 1, desiredPlayers: 2, maximumPlayers: 4,
      gameMinimumPlayers: 1, gameMaximumPlayers: 4, canTeachRules: false, knownExpansions: [], selectedExpansionIds: [],
      gathering: { bggId: 42 }, confirmedParticipants: [
        { id: "organizer-id", name: "Антон", originalName: "Антон", isOrganizer: true },
        { id: "participant-id", name: "Виктор", originalName: "Виктор", isOrganizer: false }], waitlistedParticipants: [] } as unknown as GatheringDetail;
    await act(async () => root.render(<EditGathering community={{ key: "club", name: "Клуб", mode: "Club", timeZoneId: "UTC" }}
      id="gathering-id" value={value} done={done} cancel={() => {}} />));
    await click("Виктор"); await click("✏️ Изменить имя"); await input("Виктор (новичок)"); await click("Применить имя");
    expect(mocks.mutation).not.toHaveBeenCalled();
    expect(container.querySelector<HTMLInputElement>('input[type="datetime-local"]')!.value).toBe("2070-01-01T18:00");
    await click("Сохранить");
    expect(mocks.mutation).toHaveBeenCalledTimes(1);
    const [url, options] = mocks.mutation.mock.calls[0]; expect(url).toBe("/gatherings/gathering-id");
    expect(JSON.parse(options.body).participantNames).toEqual([{ participantId: "participant-id", displayNameOverride: "Виктор (новичок)" }]);
    expect(done).toHaveBeenCalledTimes(1);
  });

  it("saves the play name with the same player identity, results and roster selection", async () => {
    const onSaved = vi.fn();
    mocks.api.mockResolvedValue({ revision: 2, wasPlayed: true, endedAtUtc: "2026-10-01T12:00:00Z", canEdit: true,
      canShare: false, references: [], expansions: [], players: [
        { id: "player-id", name: "Виктор + жена", originalName: "Виктор", displayNameOverride: "Виктор + жена", canRename: true, score: 7, isWinner: true },
        { id: "guest-id", name: "Гость", canRename: false, score: 3, isWinner: false }] });
    await act(async () => root.render(<PlayPanel community={{ key: "club", name: "Клуб", mode: "Club", timeZoneId: "UTC" }} id="g" onSaved={onSaved} />));
    await click("Имя в этой партии: Виктор + жена"); await click("↩️ Использовать исходное имя"); await click("Сохранить запись");
    const [, options] = mocks.api.mock.calls.find(([, options]) => options?.method === "PUT")!;
    const body = JSON.parse(options.body);
    expect(body.participantNames).toEqual([{ participantId: "player-id", displayNameOverride: null }]);
    expect(body.playerResults).toEqual([{ playerId: "player-id", score: 7, isWinner: true }, { playerId: "guest-id", score: 3, isWinner: false }]);
    expect(onSaved).toHaveBeenCalledTimes(1);
  });
});
