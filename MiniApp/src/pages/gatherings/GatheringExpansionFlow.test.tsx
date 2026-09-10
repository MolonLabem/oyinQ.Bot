// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { ClubGame, GatheringDetail } from "../../api/types";

vi.mock("../../hooks/useAsync", () => ({ useAsync: () => ({ data: [], loading: false, reload: vi.fn() }) }));
vi.mock("../../components/Wishlist", () => ({ WishButton: () => null }));
vi.mock("../../components/GameProviderNotice", () => ({ GameProviderNotice: () => null }));
vi.mock("../../telegram/webApp", () => ({ telegram: { success: vi.fn() }, successEventName: "success" }));
vi.mock("../../api/client", async importOriginal => ({ ...await importOriginal<object>(), gatheringMutation: vi.fn() }));
vi.mock("../../components/GamePicker", () => ({
  GameMeta: () => null,
  GamePicker: ({ onSelect }: { onSelect: (game: ClubGame, source: string, selected: number[]) => void }) =>
    <button onClick={() => onSelect({ bggId: 10, name: "Цивилизация", minPlayers: 2, maxPlayers: 4, expansions: [{ bggId: 20, name: "Дополнение", minPlayers: 2, maxPlayers: 5 }, { bggId: 21, name: "Второе" }] }, "bgg", [])}>Выбрать игру</button>,
}));
import { gatheringMutation } from "../../api/client";
import { CreateGathering, EditGathering } from "./GatheringsPage";

let host: HTMLDivElement;
let root: Root;
beforeEach(() => {
  vi.clearAllMocks();
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-09-09T12:00:00Z"));
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.useRealTimers(); });

it.each((["Club", "Camp"] as const).flatMap(mode => ["create", "edit"].map(screen => ({ mode, screen }))))("$mode $screen supports multiple expansions and independent ownership", async ({ mode, screen }) => {
  const community = { key: mode.toLowerCase(), name: mode, mode, timeZoneId: "UTC", startsAtUtc: "2030-09-10T00:00:00Z", endsAtUtc: "2030-09-12T00:00:00Z" };
  const value = { startsAtLocal: "2030-09-10T18:00", minimumPlayers: 2, desiredPlayers: 4, maximumPlayers: 4,
    gameBaseMinimumPlayers: 2, gameBaseMaximumPlayers: 4, knownExpansions: [{ bggId: 20, name: "Дополнение", minPlayers: 2, maxPlayers: 5 }, { bggId: 21, name: "Второе" }], selectedExpansionIds: [], canTeachRules: true } as unknown as GatheringDetail;
  await act(async () => root.render(screen === "create"
    ? <CreateGathering community={community} bggAvailable onDone={() => {}} editRegistration={() => {}} />
    : <EditGathering community={community} id="g" value={value} done={() => {}} cancel={() => {}} />));
  const button = (text: string) => [...host.querySelectorAll("button")].find(x => x.textContent === text)!;
  if (screen === "create") await act(async () => button("Выбрать игру").click());
  const selects = () => [...host.querySelectorAll<HTMLInputElement>('fieldset input[type="checkbox"]:not([aria-label])')];
  await act(async () => selects()[0].click());
  await act(async () => selects()[1].click());
  await act(async () => host.querySelector<HTMLInputElement>('[aria-label="Добавить в мою коллекцию: Дополнение"]')!.click());
  if (mode === "Camp") {
    await act(async () => host.querySelector<HTMLInputElement>('[aria-label="Я привезу: Второе"]')!.click());
    expect(host.querySelector<HTMLInputElement>('[aria-label="Добавить в мою коллекцию: Второе"]')!.checked).toBe(true);
  } else {
    expect(host.querySelector('[aria-label^="Я привезу:"]')).toBeNull();
    expect(host.textContent).not.toContain("кэмп");
    expect(host.querySelector<HTMLInputElement>('[aria-label="Добавить в мою коллекцию: Второе"]')!.checked).toBe(false);
  }
  expect(gatheringMutation).not.toHaveBeenCalled();
  await act(async () => {
    const date = host.querySelector<HTMLInputElement>('input[type="datetime-local"]')!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, "2030-09-10T18:00");
    date.dispatchEvent(new Event("input", { bubbles: true }));
  });
  await act(async () => button(screen === "create" ? "Создать сбор" : "Сохранить").click());
  const lastBody = () => JSON.parse(vi.mocked(gatheringMutation).mock.calls.at(-1)![1]!.body as string);
  expect(lastBody()).toMatchObject({ selectedExpansionIds: [20, 21], addExpansionToCollectionIds: [20], bringExpansionIds: mode === "Camp" ? [21] : [] });
  await act(async () => selects()[1].click());
  await act(async () => button(screen === "create" ? "Создать сбор" : "Сохранить").click());
  expect(lastBody()).toMatchObject({ selectedExpansionIds: [20], addExpansionToCollectionIds: [20], bringExpansionIds: [] });
  await act(async () => button("Только базовая игра").click());
  await act(async () => button(screen === "create" ? "Создать сбор" : "Сохранить").click());
  expect(lastBody()).toMatchObject({ selectedExpansionIds: [], addExpansionToCollectionIds: [], bringExpansionIds: [] });
});

it.each(["create", "edit"])("%s offers and submits five players only with the expansion", async screen => {
  const community = { key: "club", name: "Клуб", mode: "Club" as const, timeZoneId: "UTC" };
  const value = { startsAtLocal: "2030-09-10T18:00", minimumPlayers: 2, desiredPlayers: 4, maximumPlayers: 4,
    gameMinimumPlayers: 2, gameMaximumPlayers: 4, gameBaseMinimumPlayers: 2, gameBaseMaximumPlayers: 4,
    knownExpansions: [{ bggId: 20, name: "Дополнение", minPlayers: 2, maxPlayers: 5 }], selectedExpansionIds: [], canTeachRules: true } as unknown as GatheringDetail;
  await act(async () => root.render(screen === "create"
    ? <CreateGathering community={community} bggAvailable onDone={() => {}} editRegistration={() => {}} />
    : <EditGathering community={community} id="g" value={value} done={() => {}} cancel={() => {}} />));
  const button = (text: string) => [...host.querySelectorAll("button")].find(item => item.textContent === text)!;
  if (screen === "create") await act(async () => button("Выбрать игру").click());
  const limits = () => [...host.querySelectorAll<HTMLSelectElement>(".limits select")];
  expect(limits()[2].value).toBe("4");
  expect([...limits()[2].options].map(option => option.value)).not.toContain("5");
  const expansion = host.querySelector<HTMLInputElement>('fieldset input[type="checkbox"]')!;
  await act(async () => expansion.click());
  expect(limits()[2].value).toBe("5");
  await act(async () => { limits()[1].value = "5"; limits()[1].dispatchEvent(new Event("change", { bubbles: true })); });
  await act(async () => {
    const date = host.querySelector<HTMLInputElement>('input[type="datetime-local"]')!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, "2030-09-10T18:00");
    date.dispatchEvent(new Event("input", { bubbles: true }));
  });
  await act(async () => button(screen === "create" ? "Создать сбор" : "Сохранить").click());
  const sent = JSON.parse(vi.mocked(gatheringMutation).mock.calls[0][1]!.body as string);
  expect(sent).toMatchObject({ selectedExpansionIds: [20], desiredPlayers: 5, maximumPlayers: 5 });
  await act(async () => expansion.click());
  expect(limits()[2].value).toBe("4");
  expect(limits()[1].value).toBe("4");
});
