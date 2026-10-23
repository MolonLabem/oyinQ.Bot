// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn(), confirm: vi.fn(), success: vi.fn() }, successEventName: "success" }));
import { api } from "../../api/client";
import type { Community, PersonalCollectionItem } from "../../api/types";
import { telegram } from "../../telegram/webApp";
import { CampProfileProvider } from "./CampProfileContext";
import { ProfileCollectionPage } from "./ProfileCollectionPage";

const base: PersonalCollectionItem = { bggId: 42, itemType: "BaseGame", source: "Manual", snapshot: { name: "Немезида" } };
const other: PersonalCollectionItem = { ...base, bggId: 43, snapshot: { name: "Азул" } };
const expansion: PersonalCollectionItem = { ...base, bggId: 44, itemType: "Expansion", snapshot: { name: "Дополнение", parentBggIds: [42, 43] } };
let host: HTMLDivElement; let root: Root; let items: PersonalCollectionItem[];
const deletes = () => vi.mocked(api).mock.calls.filter(call => call[1]?.method === "DELETE");
const checks = () => [...host.querySelectorAll<HTMLInputElement>('.collection-select input')];
function button(text: string) {
  const result = [...host.querySelectorAll<HTMLButtonElement>("button")].find(b => b.textContent === text);
  expect(result, text).toBeTruthy(); return result!;
}
async function click(text: string) { await act(async () => button(text).click()); }
async function search(value: string) {
  const input = host.querySelector<HTMLInputElement>('.collection-search input[type="search"]')!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
async function mount(community?: Community) {
  const page = <ProfileCollectionPage community={community} bggAvailable={false} />;
  await act(async () => root.render(community?.mode === "Camp" ? <CampProfileProvider community={community}>{page}</CampProfileProvider> : page));
}
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.clearAllMocks(); localStorage.clear(); sessionStorage.clear();
  history.replaceState({}, "", "/");
  vi.spyOn(window, "scrollTo").mockImplementation(() => {}); HTMLElement.prototype.scrollIntoView = vi.fn();
  vi.mocked(telegram.confirm).mockResolvedValue(true);
  items = [base, other, expansion];
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (init?.method === "DELETE") {
      items = items.filter(item => path !== `/profile/collection/${item.itemType}/${item.bggId}`); return undefined;
    }
    if (path === "/profile/collection/") return [...items];
    if (path.endsWith("/imports/source")) return { source: null };
    if (path.includes("view=settings")) return { canAct: true, myDates: ["2026-10-22"], declinedGameIds: [], suggestedGameIds: [] };
    if (path.startsWith("/camp/contributions")) return [{ ...base, commitment: "Bringing", allAttendanceDays: true }];
    return [];
  });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });

it("deletes directly only after confirmation, even without a community or BGG", async () => {
  await mount();
  expect(host.querySelector(".collection-menu")).toBeNull(); expect(deletes()).toHaveLength(0);
  const remove = () => host.querySelector<HTMLButtonElement>('button[aria-label="Удалить «Немезида» из коллекции"]')!;
  expect(remove().querySelector('svg[aria-hidden="true"]')).not.toBeNull();
  vi.mocked(telegram.confirm).mockResolvedValueOnce(false);
  await act(async () => remove().click());
  expect(deletes()).toHaveLength(0); expect(remove()).toBeTruthy();
  await act(async () => remove().click());
  expect(telegram.confirm).toHaveBeenLastCalledWith("Удалить «Немезида» из коллекции во всех сообществах?");
  expect(deletes().map(call => call[0])).toEqual(["/profile/collection/BaseGame/42"]);
  expect(host.querySelector('[data-box-game="BaseGame:42"]')).toBeNull();
  expect(host.querySelector('[data-box-game="Expansion:44"]')).toBeTruthy();
});

it("selects games and expansions independently and deduplicates an expansion shared by two bases", async () => {
  await mount({ key: "club", name: "Клуб", mode: "Club", timeZoneId: "UTC" }); await click("Выбрать");
  expect(button("Удалить (0)").disabled).toBe(true);
  expect([...host.querySelectorAll('details.collection-expansions')].every(x => x.hasAttribute("open"))).toBe(true);
  expect(host.querySelector(".box-control")).toBeNull();
  await act(async () => checks()[0].click());
  expect(checks().filter(x => x.checked)).toHaveLength(1);
  await click("Выбрать все"); expect(host.textContent).toContain("Выбрано: 3");
  await click("Снять выбор"); expect(checks().every(x => !x.checked)).toBe(true);
  await click("Выбрать все"); await click("Удалить (3)");
  expect(telegram.confirm).toHaveBeenCalledTimes(1);
  expect(telegram.confirm).toHaveBeenCalledWith("Удалить выбранные игры (3) из коллекции во всех сообществах?");
  expect(deletes().map(call => call[0]).sort()).toEqual([
    "/profile/collection/BaseGame/42", "/profile/collection/BaseGame/43", "/profile/collection/Expansion/44"
  ]);
  expect(host.textContent).toContain("В коллекции пока нет игр");
});

it("clears selections hidden by search and selects only the current results", async () => {
  items = [base, other]; await mount(); await click("Выбрать"); await click("Выбрать все");
  await search("Азул"); expect(host.textContent).toContain("Выбрано: 1"); expect(checks()).toHaveLength(1);
  await search(""); expect(checks().filter(x => x.checked)).toHaveLength(1);
  await search("Азул"); await click("Снять выбор"); await click("Выбрать все"); await click("Удалить (1)");
  expect(deletes().map(call => call[0])).toEqual(["/profile/collection/BaseGame/43"]);
  expect(items).toEqual([base]);
});

it("uses the Camp filter for selection and retains the server restriction on active boxes", async () => {
  const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (init?.method === "DELETE") throw new Error("Сначала уберите игру из доступных в активных кэмпах.");
    return previous(path, init);
  });
  await mount({ key: "camp", name: "Кэмп", mode: "Camp", timeZoneId: "UTC" });
  await click("Выбрать"); await click("Выбрать все"); await click("Точно привезу");
  expect(host.textContent).toContain("Выбрано: 2");
  // The matching base still shows its independently selectable expansion.
  await click("Снять выбор"); await act(async () => checks()[0].click()); await click("Удалить (1)");
  expect(host.textContent).toContain("«Немезида»: Сначала уберите игру из доступных в активных кэмпах.");
  expect(checks()[0].checked).toBe(true); expect(items).toHaveLength(3);
  expect([...host.querySelectorAll<HTMLSelectElement>('.box-control select')].every(x => x.disabled)).toBe(true);
});

it("cancels selection through Cancel or Telegram Back without deleting anything", async () => {
  await mount(); await click("Выбрать"); await click("Выбрать все"); await click("Отмена");
  expect(checks()).toHaveLength(0); await click("Выбрать");
  expect(host.textContent).toContain("Выбрано: 0");
  const back = vi.mocked(telegram.back).mock.calls.at(-1)![1];
  await act(async () => back());
  expect(checks()).toHaveLength(0); expect(deletes()).toHaveLength(0); expect(telegram.confirm).not.toHaveBeenCalled();
});

it("keeps the selection when confirmation is cancelled", async () => {
  await mount(); await click("Выбрать"); await click("Выбрать все");
  vi.mocked(telegram.confirm).mockResolvedValueOnce(false); await click("Удалить (3)");
  expect(host.textContent).toContain("Выбрано: 3"); expect(button("Удалить (3)").disabled).toBe(false);
  expect(deletes()).toHaveLength(0);
});

it("prevents repeated confirmation and deletion while a request is pending", async () => {
  let confirm!: (value: boolean) => void;
  vi.mocked(telegram.confirm).mockImplementationOnce(() => new Promise(resolve => { confirm = resolve; }));
  await mount(); const remove = host.querySelector<HTMLButtonElement>('.collection-delete')!;
  await act(async () => { remove.click(); remove.click(); });
  expect(telegram.confirm).toHaveBeenCalledTimes(1); expect(remove.disabled).toBe(true); expect(deletes()).toHaveLength(0);
  let finish!: () => void; const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (init?.method === "DELETE") await new Promise<void>(resolve => { finish = resolve; });
    return previous(path, init);
  });
  await act(async () => confirm(true));
  expect(deletes()).toHaveLength(1); expect(remove.disabled).toBe(true);
  await act(async () => { remove.click(); finish(); });
  expect(deletes()).toHaveLength(1);
});

it("retains blocked items after partial deletion and retries only those items when a refresh fails", async () => {
  items = [base, other]; let block = true; let failRefresh = false;
  const previous = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (path === "/profile/collection/" && failRefresh) throw new Error("Не удалось обновить список");
    if (init?.method === "DELETE") {
      if (path.endsWith("/42") && block) throw new Error("Сначала снимите предложение на кэмпе.");
      failRefresh = true;
    }
    return previous(path, init);
  });
  await mount(); await click("Выбрать"); await click("Выбрать все"); await click("Удалить (2)");
  expect(host.textContent).toContain("«Немезида»: Сначала снимите предложение на кэмпе.");
  expect(host.textContent).toContain("Не удалось обновить список");
  expect(host.textContent).toContain("Выбрано: 1"); expect(checks()[0].checked).toBe(true);
  expect(host.querySelector('[data-box-game="BaseGame:43"]')).toBeNull();
  block = false; failRefresh = false; await click("Удалить (1)");
  expect(deletes().map(call => call[0])).toEqual([
    "/profile/collection/BaseGame/42", "/profile/collection/BaseGame/43", "/profile/collection/BaseGame/42"
  ]);
  expect(checks()).toHaveLength(0);
});

function largeCollection() {
  items = Array.from({ length: 23 }, (_, index) => ({ ...base, bggId: index + 1, snapshot: { name: `Игра ${index + 1}` } }));
}
const cards = () => host.querySelectorAll(".collection-results > .stack > .card");
async function nextPage() { await act(async () => host.querySelector<HTMLButtonElement>('[aria-label="Следующая страница"]')!.click()); }

it("defaults to ten games, retains selection across pages, and remembers a changed page size", async () => {
  largeCollection(); await mount();
  expect(cards()).toHaveLength(10); expect(host.textContent).toContain("1–10 из 23");
  await click("Выбрать"); await act(async () => checks()[0].click()); await nextPage();
  expect(host.textContent).toContain("Страница 2 из 3"); expect(checks().some(x => x.checked)).toBe(false);
  await act(async () => checks()[0].click()); expect(host.textContent).toContain("Выбрано: 2");
  await click("Выбрать страницу"); expect(host.textContent).toContain("Выбрано: 11");
  await act(async () => host.querySelector<HTMLButtonElement>('[aria-label="Предыдущая страница"]')!.click());
  expect(checks()[0].checked).toBe(true);
  await nextPage();
  await act(async () => host.querySelector<HTMLInputElement>('.collection-page-size input[value="20"]')!.click());
  expect(cards()).toHaveLength(20); expect(host.textContent).toContain("Выбрано: 11");
  expect(host.textContent).toContain("Страница 1 из 2");
  await act(async () => root.render(null)); await mount();
  expect(cards()).toHaveLength(20); expect(deletes()).toHaveLength(0);
  expect(host.querySelector<HTMLInputElement>('.collection-page-size input[value="20"]')!.checked).toBe(true);
});

it("searches the entire collection and returns to page one when the query changes", async () => {
  largeCollection(); items[22].snapshot.name = "Уникальная коробка"; await mount(); await nextPage(); await nextPage();
  expect(cards()).toHaveLength(3); await search("Уникальная");
  expect(cards()).toHaveLength(1); expect(host.querySelector('[data-box-game="BaseGame:23"]')).not.toBeNull();
  expect(host.querySelector(".collection-pagination")).toBeNull();
  await search(""); expect(cards()).toHaveLength(10); expect(host.textContent).toContain("Страница 1 из 3");
});

it("opens a linked game on its page, keeps expansions with their base, and clamps the last page after deletion", async () => {
  largeCollection(); items.push({ ...expansion, bggId: 900, snapshot: { name: "Дополнение", parentBggIds: [1, 23] } });
  history.replaceState({}, "", "/?box=23"); await mount();
  expect(host.textContent).toContain("Страница 3 из 3"); expect(cards()).toHaveLength(3);
  expect(host.querySelector('[data-box-game="Expansion:900"]')).not.toBeNull();
  await click("Выбрать"); await click("Выбрать страницу"); await click("Удалить (4)");
  expect(host.textContent).toContain("Страница 2 из 2"); expect(cards()).toHaveLength(10);
  expect(items).toHaveLength(20);
});

it("restores the saved Camp filter page after its availability data arrives", async () => {
  largeCollection(); sessionStorage.setItem("oyinq-collection-filter:camp", "Bringing"); sessionStorage.setItem("oyinq-collection-page:camp", "2");
  const previous = vi.mocked(api).getMockImplementation()!;
  let finish!: (value: unknown) => void;
  vi.mocked(api).mockImplementation((path, options) => path.startsWith("/camp/contributions")
    ? new Promise(resolve => { finish = resolve; }) : previous(path, options));
  await mount({ key: "camp", name: "Кэмп", mode: "Camp", timeZoneId: "UTC" });
  await act(async () => finish(items.map(item => ({ ...item, commitment: "Bringing", allAttendanceDays: true }))));
  expect(host.textContent).toContain("Страница 2 из 3"); expect(cards()).toHaveLength(10);
});
