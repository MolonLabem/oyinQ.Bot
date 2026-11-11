// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { ClubGame, Community, GatheringDetail } from "../../api/types";
import { changeDateTime, readDateTime } from "../../components/dateTimeTestHelper";

vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn(), gatheringMutation: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn(), success: vi.fn(), confirm: vi.fn(async () => true) }, successEventName: "success" }));
vi.mock("../../components/GamePicker", () => ({ GameMeta: () => null, GamePicker: () => <p>Выберите игру</p> }));
import { api, gatheringMutation } from "../../api/client";
import { telegram } from "../../telegram/webApp";
import { profileWasDeleted } from "../../app/profileLifecycle";
import { CreateGathering, GatheringsPage } from "./GatheringsPage";
import { clearGatheringDraft, readGatheringDraft, saveGatheringDraft, type GatheringDraft } from "./gatheringDraft";
import { creationOperation } from "./gatheringCreationOperation";

const game: ClubGame = { bggId: 10, name: "Дюна", minPlayers: 2, maxPlayers: 4, expansions: [{ bggId: 20, name: "Иксианцы" }] };
const club: Community = { key: "draft-club", name: "Клуб", mode: "Club", timeZoneId: "UTC" };
const camp: Community = { key: "draft-camp", name: "Кэмп", mode: "Camp", timeZoneId: "UTC", startsAtUtc: "2030-09-10T00:00:00Z", endsAtUtc: "2030-09-12T00:00:00Z" };
let host: HTMLDivElement;
let root: Root;
beforeEach(() => {
  vi.clearAllMocks(); sessionStorage.clear();
  vi.useFakeTimers({ toFake: ["Date"] }); vi.setSystemTime(new Date("2030-09-09T12:00:00Z"));
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  Element.prototype.scrollIntoView = vi.fn();
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.mocked(api).mockImplementation(async path => path.startsWith("/games?") ? [game]
    : path.startsWith("/catalog/10/provider?") ? { isOwned: true, isConfirmed: true, summary: "Есть у вас", providers: [] }
    : path.startsWith("/bgg/game?") ? { game, expansions: game.expansions } : { items: [], hasNext: false, hasPrevious: false });
  vi.mocked(gatheringMutation).mockResolvedValue({});
  vi.mocked(telegram.confirm).mockResolvedValue(true);
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); vi.useRealTimers(); });
const button = (text: string) => [...host.querySelectorAll("button")].find(item => item.textContent === text)!;
async function description(value: string) {
  await act(async () => {
    const input = host.querySelector("textarea")!;
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
async function leave() { await act(async () => root.render(<div>Профиль</div>)); }
async function create(community = club, onDone = vi.fn(), initialGameId?: number, copy?: GatheringDetail) {
  await act(async () => root.render(<CreateGathering community={community} initialGameId={initialGameId} copy={copy} bggAvailable onDone={onDone} editRegistration={() => {}} />));
}

it.each([club, camp])("resumes all edited fields after leaving $mode creation and isolates communities", async community => {
  await create(community, vi.fn(), game.bggId);
  await changeDateTime(host, "2030-09-10T18:30"); await description("Новичкам объясню правила");
  await act(async () => [...host.querySelectorAll("label")].find(label => label.textContent?.startsWith("Иксианцы"))!.querySelector("input")!.click());
  await act(async () => host.querySelector<HTMLInputElement>('[aria-label="Добавить в мою коллекцию: Иксианцы"]')!.click());
  const saved = readGatheringDraft(community.key)!;
  expect(saved).toMatchObject({ chosen: { bggId: 10 }, starts: "2030-09-10T18:30", expansions: [20], expansionOwnership: { add: [20], bring: [] } });
  await leave(); await create({ ...community, key: "different-community" });
  expect(host.querySelector("textarea")!.value).toBe(""); expect(host.textContent).not.toContain("Дюна");
  await leave(); await create(community);
  expect(host.querySelector("textarea")!.value).toBe("Новичкам объясню правила");
  expect(readDateTime(host)).toBe("2030-09-10T18:30");
  expect(host.querySelector<HTMLInputElement>('[aria-label="Добавить в мою коллекцию: Иксианцы"]')!.checked).toBe(true);
  expect(host.textContent).toContain("Изменить игру");
  expect(gatheringMutation).not.toHaveBeenCalled();
});

it("shows a resume action and clears the draft only after successful creation", async () => {
  await create(club, vi.fn(), game.bggId); await changeDateTime(host, "2030-09-10T18:30"); await description("Сохранить меня"); await leave();
  await act(async () => root.render(<GatheringsPage community={club} bggAvailable onInitialConsumed={() => {}} editRegistration={() => {}} openCollection={() => {}} />));
  await act(async () => button("Продолжить сбор").click());
  expect(host.querySelector("textarea")!.value).toBe("Сохранить меня");
  await act(async () => button("Создать сбор").click());
  expect(readGatheringDraft(club.key)).toBeUndefined();
  await act(async () => [...host.querySelectorAll("button")].find(item => item.textContent === "Создать сбор")!.click());
  expect(host.textContent).toContain("Выберите игру"); expect(host.textContent).not.toContain("Изменить игру");
});

it("keeps a failed submission and its retry identity across navigation", async () => {
  vi.mocked(gatheringMutation).mockRejectedValueOnce(new Error("Нет связи"));
  await create(club, vi.fn(), game.bggId); await changeDateTime(host, "2030-09-10T18:30"); await description("Попробовать снова");
  await act(async () => button("Создать сбор").click());
  expect(host.textContent).toContain("Нет связи");
  const first = JSON.parse(vi.mocked(gatheringMutation).mock.calls[0][1]!.body as string);
  await leave(); await create(); await act(async () => button("Создать сбор").click());
  const retry = JSON.parse(vi.mocked(gatheringMutation).mock.calls[1][1]!.body as string);
  expect(retry).toEqual(first); expect(readGatheringDraft(club.key)).toBeUndefined();
});

it("does not replace a saved draft with a preselected game without confirmed discard", async () => {
  await create(club, vi.fn(), game.bggId); await description("Мой черновик"); await leave();
  await create(club, vi.fn(), 99);
  expect(host.querySelector("textarea")!.value).toBe("Мой черновик");
  expect(api).not.toHaveBeenCalledWith(expect.stringContaining("/catalog/demand/99"));
  vi.mocked(telegram.confirm).mockResolvedValueOnce(false);
  await act(async () => button("Начать новый сбор").click());
  expect(readGatheringDraft(club.key)!.description).toBe("Мой черновик");
  await act(async () => button("Удалить черновик").click());
  expect(readGatheringDraft(club.key)).toBeUndefined();
});

it("retains repeat-gathering intent after leaving the form", async () => {
  const copy = { gathering: { publicId: "original", bggId: 10, gameName: "Дюна" }, gameMinimumPlayers: 2, gameMaximumPlayers: 4,
    minimumPlayers: 2, desiredPlayers: 3, maximumPlayers: 4, selectedExpansionIds: [20], knownExpansions: game.expansions,
    description: "Повтор", canTeachRules: false } as GatheringDetail;
  await create(club, vi.fn(), undefined, copy); await changeDateTime(host, "2030-09-10T18:30"); await leave(); await create();
  await act(async () => button("Создать сбор").click());
  expect(JSON.parse(vi.mocked(gatheringMutation).mock.calls[0][1]!.body as string)).toMatchObject({ copyFromPublicId: "original", selectedExpansionIds: [20], desiredPlayers: 3, canTeachRules: false });
});

it("a late success cannot close a resumed form or remove its newer draft and operation", async () => {
  let finish!: () => void;
  vi.mocked(gatheringMutation).mockImplementationOnce(() => new Promise(resolve => { finish = () => resolve({}); }));
  const oldDone = vi.fn(); const newDone = vi.fn();
  await create(club, oldDone, game.bggId); await changeDateTime(host, "2030-09-10T18:30");
  await act(async () => button("Создать сбор").click()); await leave(); await create(club, newDone); await description("Новые данные");
  const newerBody = { description: "Новые данные" }; const newerOperation = creationOperation(club.key, newerBody);
  await act(async () => finish());
  expect(oldDone).not.toHaveBeenCalled(); expect(newDone).not.toHaveBeenCalled();
  expect(readGatheringDraft(club.key)!.description).toBe("Новые данные");
  expect(creationOperation(club.key, newerBody)).toBe(newerOperation);
});

it("a late success retains the identity for a resumed unchanged submission", async () => {
  let finish!: () => void;
  vi.mocked(gatheringMutation).mockImplementationOnce(() => new Promise(resolve => { finish = () => resolve({}); }));
  await create(club, vi.fn(), game.bggId); await changeDateTime(host, "2030-09-10T18:30");
  await act(async () => button("Создать сбор").click()); await leave(); await create();
  await act(async () => finish()); await act(async () => button("Создать сбор").click());
  const bodies = vi.mocked(gatheringMutation).mock.calls.map(call => JSON.parse(call[1]!.body as string));
  expect(bodies[1].operationId).toBe(bodies[0].operationId);
});

it("uses the newest draft when storage writes fail, and clears the fallback on erasure", async () => {
  await create(club, vi.fn(), game.bggId); await description("Старое значение");
  vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new DOMException("Quota", "QuotaExceededError"); });
  await description("Новое значение"); await leave(); await create();
  expect(host.querySelector("textarea")!.value).toBe("Новое значение");
  await leave(); profileWasDeleted();
  expect(readGatheringDraft(club.key)).toBeUndefined();
});

it("does not revive a discarded draft when storage removal fails", async () => {
  await create(club, vi.fn(), game.bggId);
  vi.spyOn(Storage.prototype, "removeItem").mockImplementation(() => { throw new Error("Blocked"); });
  clearGatheringDraft(club.key);
  expect(readGatheringDraft(club.key)).toBeUndefined();
});

it("ignores malformed or older storage and never deletes a newer saved draft", () => {
  sessionStorage.setItem("oyinq-gathering-draft:broken", '{"version":1,"draft":{"chosen":{}}}');
  expect(readGatheringDraft("broken")).toBeUndefined();
  const draft: GatheringDraft = { source: "catalog", chosen: game, expansions: [], expansionOwnership: { add: [], bring: [] }, addToCollection: false, bringToCamp: false, starts: "", minimum: 2, desired: 4, maximum: 4, description: "old", teach: true };
  saveGatheringDraft("different-draft", { ...draft, description: "new" }); clearGatheringDraft("different-draft", draft);
  expect(readGatheringDraft("different-draft")!.description).toBe("new");
});

it("locks real provider ownership controls while submitting a Camp gathering", async () => {
  let finish!: () => void;
  vi.mocked(gatheringMutation).mockImplementationOnce(() => new Promise(resolve => { finish = () => resolve({}); }));
  await create(camp, vi.fn(), game.bggId); await changeDateTime(host, "2030-09-10T18:30");
  const bring = [...host.querySelectorAll("label")].find(label => label.textContent === "Я привезу базовую игру на этот кэмп")!.querySelector("input")!;
  expect(bring.matches(":disabled")).toBe(false);
  await act(async () => button("Создать сбор").click());
  expect(bring.matches(":disabled")).toBe(true);
  await act(async () => bring.click());
  expect(bring.checked).toBe(false);
  await act(async () => finish());
  expect(readGatheringDraft(camp.key)).toBeUndefined();
  expect(JSON.parse(vi.mocked(gatheringMutation).mock.calls[0][1]!.body as string).bringToCamp).toBe(false);
});

it("gives an explicitly new form a new identity after a background success", async () => {
  let finish!: () => void;
  vi.mocked(gatheringMutation).mockImplementationOnce(() => new Promise(resolve => { finish = () => resolve({}); }));
  await create(club, vi.fn(), game.bggId); await changeDateTime(host, "2030-09-10T18:30");
  await act(async () => button("Создать сбор").click()); await leave(); await act(async () => finish());
  expect(readGatheringDraft(club.key)).toBeUndefined();
  await create(club, vi.fn(), game.bggId); await changeDateTime(host, "2030-09-10T18:30");
  await act(async () => button("Создать сбор").click());
  const bodies = vi.mocked(gatheringMutation).mock.calls.map(call => JSON.parse(call[1]!.body as string));
  expect(bodies[1].operationId).not.toBe(bodies[0].operationId);
});
