// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { ClubGame } from "../../api/types";

vi.mock("../../components/Wishlist", () => ({ WishButton: () => null }));
vi.mock("../../telegram/webApp", () => ({ telegram: { success: vi.fn() }, successEventName: "success" }));
vi.mock("../../api/client", async importOriginal => ({ ...await importOriginal<object>(), api: vi.fn(), gatheringMutation: vi.fn() }));
vi.mock("../../components/GamePicker", () => ({
  GameMeta: () => null,
  GamePicker: ({ onSelect }: { onSelect: (game: ClubGame, source: string, selected: number[]) => void }) =>
    <button onClick={() => onSelect({ bggId: 42, name: "Escape Plan", minPlayers: 1, maxPlayers: 5, isOwned: true, expansions: [] }, "catalog", [])}>Выбрать игру</button>,
}));
import { api, gatheringMutation } from "../../api/client";
import { CreateGathering } from "./GatheringsPage";

let host: HTMLDivElement;
let root: Root;
beforeEach(() => {
  vi.clearAllMocks();
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-09-10T12:00:00Z"));
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.mocked(api).mockImplementation(async path => path.includes("/provider?")
    ? { isOwned: true, isConfirmed: false, canBring: true, providers: [], summary: "Вы можете привезти свою коробку" } : []);
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.useRealTimers(); });

it("retries a lost creation response with the same operation identity", async () => {
  const game = { bggId: 42, name: "Escape Plan", minPlayers: 1, maxPlayers: 5, expansions: [] };
  vi.mocked(api).mockImplementation(async path => path.startsWith("/games?") ? [game] : { providers: [], isConfirmed: true, summary: "Есть в клубе" });
  vi.mocked(gatheringMutation).mockRejectedValueOnce(new Error("lost response")).mockResolvedValueOnce({ publicId: "saved" });
  const done = vi.fn();
  await act(async () => root.render(<CreateGathering initialGameId={42} community={{ key: "retry-club", name: "Клуб", mode: "Club", timeZoneId: "UTC" }} bggAvailable={false} onDone={done} editRegistration={() => {}} />));
  await act(async () => {
    const date = host.querySelector<HTMLInputElement>('input[type="datetime-local"]')!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, "2026-09-26T12:00");
    date.dispatchEvent(new Event("input", { bubbles: true }));
  });
  const create = () => [...host.querySelectorAll("button")].find(x => x.textContent === "Создать сбор")!;
  await act(async () => create().click()); expect(done).not.toHaveBeenCalled();
  await act(async () => create().click());
  const bodies = vi.mocked(gatheringMutation).mock.calls.map(call => JSON.parse(String(call[1]?.body)));
  expect(bodies[0].operationId).toMatch(/^[a-f0-9-]{36}$/);
  expect(bodies[0].operationId).toBe(bodies[1].operationId); expect(done).toHaveBeenCalledOnce();
});

it("selects a demanded game absent from the personal catalog", async () => {
  vi.mocked(api).mockImplementation(async path => path.startsWith("/catalog/demand?")
    ? [{ game: { bggId: 77, name: "Игра из спроса", minPlayers: 2, maxPlayers: 4, expansions: [] } }]
    : path.startsWith("/games?") ? [] : { providers: [], isConfirmed: false, summary: "Нет коробки" });
  await act(async () => root.render(<CreateGathering initialGameId={77} community={{ key: "demand", name: "Клуб", mode: "Club", timeZoneId: "UTC" }} bggAvailable={false} onDone={() => {}} editRegistration={() => {}} />));
  expect(host.textContent).toContain("Игра из спроса");
  expect(host.textContent).not.toContain("Игра больше не доступна");
  expect([...host.querySelectorAll("button")].find(x => x.textContent === "Создать сбор")!.disabled).toBe(false);
});

it.each([false, true])("saved owned Camp game submits explicit bringing choice: %s", async bring => {
  await act(async () => root.render(<CreateGathering
    community={{ key: "camp", name: "Кэмп", mode: "Camp", timeZoneId: "UTC", startsAtUtc: "2026-09-26T00:00:00Z", endsAtUtc: "2026-09-29T00:00:00Z" }}
    bggAvailable={false} onDone={() => {}} editRegistration={() => {}} />));
  const button = (text: string) => [...host.querySelectorAll("button")].find(item => item.textContent === text)!;
  await act(async () => button("Выбрать игру").click());
  const label = [...host.querySelectorAll("label")].find(item => item.textContent?.includes("Я привезу"))!;
  const checkbox = label.querySelector("input")!;
  expect(checkbox.disabled).toBe(false);
  expect(checkbox.checked).toBe(false);
  expect(host.textContent).not.toContain("Добавить выбранные дополнения в мою коллекцию");
  if (bring) await act(async () => checkbox.click());
  await act(async () => {
    const date = host.querySelector<HTMLInputElement>('input[type="datetime-local"]')!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, "2026-09-26T12:00");
    date.dispatchEvent(new Event("input", { bubbles: true }));
  });
  await act(async () => button("Создать сбор").click());
  expect(gatheringMutation).toHaveBeenCalledOnce();
  expect(JSON.parse(vi.mocked(gatheringMutation).mock.calls[0][1]!.body as string))
    .toMatchObject({ gameSource: "catalog", bggId: 42, addToCollection: false, bringToCamp: bring });
});
