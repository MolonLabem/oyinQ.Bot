// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AdminOverview } from "../../api/types";

vi.mock("../../telegram/webApp", () => ({ telegram: {
  back: vi.fn(() => () => {}), success: vi.fn(), confirm: vi.fn(), requestPeer: vi.fn(),
}, successEventName: "test:success" }));
vi.mock("../../api/client", async importOriginal => ({
  ...await importOriginal<typeof import("../../api/client")>(), api: vi.fn(), download: vi.fn(),
}));
vi.mock("./AnnouncementsPage", () => ({ AnnouncementsPage: () => <input aria-label="Текст оповещения" defaultValue="Черновик" /> }));
import { api, download } from "../../api/client";
import { telegram } from "../../telegram/webApp";
import { AdminPage } from "./AdminPage";

function overview(): AdminOverview {
  return {
    isSuperAdmin: true,
    clubs: [1, 2].map(id => ({ id, communityKey: `club-${id}`, name: `Клуб ${id}`, telegramTitle: `Группа ${id}`,
      telegramChatId: -100000 - id, timeZoneId: "Asia/Almaty", isActive: true, isApproved: true,
      gameCount: id, collectionRevision: id, updatedAt: "2026-09-01T00:00:00Z", gatherings: id })),
    camps: [3, 4].map(id => ({ id, communityKey: `camp-${id}`, name: `Кэмп ${id}`, telegramTitle: `Группа ${id}`,
      telegramChatId: -100000 - id, timeZoneId: "Asia/Almaty", isApproved: true, status: "Active",
      startsAtUtc: "2026-10-01T00:00:00Z", endsAtUtc: "2026-10-05T00:00:00Z", registrations: id, contributions: id, gatherings: id })),
    lockedCommunities: [
      { communityKey: "locked", name: "Закрытый клуб", mode: "Club", telegramChatId: -100005, isActive: true, isApproved: false },
      { name: "Новая группа", mode: "Club", telegramChatId: -100006, isActive: false, isApproved: false },
    ],
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(done => { resolve = done; });
  return { promise, resolve };
}

let root: Root;
let host: HTMLDivElement;
let data: AdminOverview;
let intercept: ((path: string, options?: RequestInit) => Promise<unknown> | undefined) | undefined;
const writes = () => vi.mocked(api).mock.calls.filter(([, options]) => options?.method && options.method !== "GET");

async function mount() { await act(async () => root.render(<AdminPage bggAvailable={false} isSuperAdmin={data.isSuperAdmin} />)); }
async function click(text: string) {
  const button = Array.from(host.querySelectorAll("button")).find(item => item.textContent?.trim() === text);
  expect(button, `Button: ${text}`).toBeDefined();
  await act(async () => button!.click());
}
async function switchTo(key: string) {
  const trigger = host.querySelector<HTMLButtonElement>('button[aria-haspopup="listbox"]')!;
  expect(trigger).not.toBeNull();
  await act(async () => trigger.click());
  const option = Array.from(host.querySelectorAll<HTMLButtonElement>('[role="option"]')).find(item => item.value === key)!;
  expect(option).toBeDefined();
  await act(async () => option.click());
}
async function changeDate(index: number, value: string) {
  const input = host.querySelectorAll<HTMLInputElement>('input[type="datetime-local"]')[index];
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
async function createCampFromKnownChat() {
  await mount();
  const card = Array.from(host.querySelectorAll(".card")).find(item => item.textContent?.includes("Новая группа"))!;
  const button = Array.from(card.querySelectorAll("button")).find(item => item.textContent === "Создать кэмп")!;
  await act(async () => button.click());
}
function title() { return host.querySelector("h1")?.textContent; }

beforeEach(() => {
  vi.clearAllMocks();
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.stubGlobal("scrollTo", vi.fn());
  const storage = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
    removeItem: (key: string) => storage.delete(key), clear: () => storage.clear(),
  });
  localStorage.clear();
  data = overview();
  intercept = undefined;
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
  vi.mocked(telegram.confirm).mockResolvedValue(true);
  vi.mocked(download).mockResolvedValue();
  vi.mocked(api).mockImplementation(async <T,>(path: string, options?: RequestInit): Promise<T> => {
    const pending = intercept?.(path, options);
    if (pending) return await pending as T;
    if (path === "/admin/overview") return data as T;
    if (options?.method) return {} as T;
    if (path.endsWith("/posting-topic")) return { isForum: false } as T;
    if (path.endsWith("/recruitment")) return { hours: 4 } as T;
    if (path.endsWith("/administrator-candidates")) return [] as T;
    if (path.endsWith("/administrators")) return [{ telegramUserId: 22, displayName: `Админ ${path.split("/")[3]}` }] as T;
    if (path.endsWith("/participants")) return { campName: `Кэмп ${path.split("/")[3]}`, participants: [] } as T;
    if (path.endsWith("/collection")) return { revision: 1, updatedAt: "2026-09-01T00:00:00Z", collection: { version: 2, games: [] } } as T;
    if (path.startsWith("/gatherings/dashboard")) return { items: [] } as T;
    throw new Error(`Unexpected request: ${path}`);
  });
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.unstubAllGlobals(); });

describe("camp creation", () => {
  it("selects a Telegram group before dates are filled and submits its selection token", async () => {
    vi.useFakeTimers({ toFake: ["setTimeout"] });
    try {
      const selectionId = "camp-selection";
      intercept = path => {
        if (path.startsWith("/admin/peer-selections")) return Promise.resolve({
          publicId: selectionId, preparedButtonId: "prepared", status: path.endsWith(selectionId) ? "Completed" : "Pending",
          result: { chat: { title: "Группа кэмпа" } },
        });
        return undefined;
      };
      vi.mocked(telegram.requestPeer).mockResolvedValue(true);
      await mount(); await click("Новый кэмп"); await click("Выбрать группу");
      await act(async () => { await vi.advanceTimersByTimeAsync(1500); });
      expect(telegram.requestPeer).toHaveBeenCalledWith("prepared");
      expect(host.querySelectorAll('[role="alert"]')).toHaveLength(0);
      expect(host.querySelector(".review-list")?.textContent).toContain("Группа кэмпа");
      await changeDate(0, "2026-10-10T18:30"); await changeDate(1, "2026-10-12T11:45");
      await click("Создать кэмп");
      const request = writes().find(([path]) => path === "/admin/camps")!;
      expect(JSON.parse(String(request[1]?.body))).toMatchObject({
        selectionId, name: "Группа кэмпа", startsAtLocal: "2026-10-10T18:30", endsAtLocal: "2026-10-12T11:45",
      });
    } finally { vi.useRealTimers(); }
  });

  it("reviews and submits exact local times for a known Telegram group", async () => {
    await createCampFromKnownChat();
    await changeDate(0, "2026-10-10T18:30");
    await changeDate(1, "2026-10-12T11:45");
    expect(host.querySelector(".review-list")?.textContent).toContain("10 октября 2026 г., 18:30");
    expect(host.querySelector(".review-list")?.textContent).toContain("12 октября 2026 г., 11:45");
    await click("Создать кэмп");
    const request = writes().find(([path]) => path === "/admin/camps")!;
    expect(JSON.parse(String(request[1]?.body))).toEqual({
      knownTelegramChatId: -100006, name: "Новая группа", startsAtLocal: "2026-10-10T18:30",
      endsAtLocal: "2026-10-12T11:45", sourceClubId: null, timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
    });
    expect(title()).not.toBe("Новый кэмп");
  });

  it("keeps invalid dates editable and submits only after correction", async () => {
    await createCampFromKnownChat();
    await click("Создать кэмп");
    expect(host.querySelectorAll('[role="alert"]')).toHaveLength(2);
    expect(writes()).toHaveLength(0);
    await changeDate(0, "2026-10-10T18:30"); await changeDate(1, "2026-10-10T18:30");
    await click("Создать кэмп");
    expect(host.textContent).toContain("Окончание должно быть позже начала.");
    expect(writes()).toHaveLength(0);
    await changeDate(1, "2026-10-10T19:30"); await click("Создать кэмп");
    expect(writes()).toHaveLength(1);
  });

  it("uses the source club timezone in the picker and retains dates after a failed save", async () => {
    await createCampFromKnownChat();
    const source = host.querySelector<HTMLSelectElement>('select')!;
    await act(async () => { source.value = "1"; source.dispatchEvent(new Event("change", { bubbles: true })); });
    expect(host.querySelector(".date-range")?.textContent).toContain("Местное время (Asia/Almaty)");
    await changeDate(0, "2026-10-10T18:30"); await changeDate(1, "2026-10-12T11:45");
    intercept = path => path === "/admin/camps" ? Promise.reject(new Error("Группа недоступна.")) : undefined;
    await click("Создать кэмп");
    expect(host.textContent).toContain("Группа недоступна.");
    expect(host.querySelectorAll<HTMLInputElement>('input[type="datetime-local"]')[0].value).toBe("2026-10-10T18:30");
    intercept = undefined;
    await click("Создать кэмп");
    const requests = writes().filter(([path]) => path === "/admin/camps");
    expect(requests).toHaveLength(2);
    expect(JSON.parse(String(requests[1][1]?.body))).toMatchObject({ sourceClubId: 1, timeZoneId: "Asia/Almaty" });
  });

  it("edits the same local-time fields and validates the range before saving", async () => {
    await mount(); await switchTo("camp-3"); await click("Настройки");
    expect(host.querySelectorAll<HTMLInputElement>('input[type="datetime-local"]')[0].value).toBe("2026-10-01T05:00");
    expect(host.querySelector(".date-range")?.textContent).toContain("Местное время (Asia/Almaty)");
    await changeDate(0, "2026-10-01T18:30"); await changeDate(1, "2026-10-01T18:00");
    await click("Сохранить настройки");
    expect(host.querySelector('[aria-invalid="true"]')).not.toBeNull();
    expect(writes()).toHaveLength(0);
    await changeDate(1, "2026-10-05T11:45"); await click("Сохранить настройки");
    expect(JSON.parse(String(writes()[0][1]?.body))).toEqual({
      name: "Кэмп 3", timeZoneId: "Asia/Almaty", startsAtLocal: "2026-10-01T18:30", endsAtLocal: "2026-10-05T11:45",
    });
  });
});

describe("persistent admin community context", () => {
  it("opens the authorized camp from the Telegram roster link instead of the saved community", async () => {
    vi.stubGlobal("location", { search: "?admin=1&adminCommunity=camp-4&adminSection=participants" });
    localStorage.setItem("oyinq-admin-community", "camp-3");
    await mount();
    expect(title()).toBe("Участники кэмпа");
    expect(api).toHaveBeenCalledWith("/admin/camps/4/participants", undefined);
    expect(api).not.toHaveBeenCalledWith("/admin/camps/3/participants", undefined);
  });
  it("does not substitute another roster when the linked camp is unauthorized", async () => {
    vi.stubGlobal("location", { search: "?admin=1&adminCommunity=missing-camp&adminSection=participants" });
    await mount();
    expect(host.textContent).toContain("Кэмп из ссылки недоступен");
    expect(vi.mocked(api).mock.calls.some(([path]) => path.endsWith("/participants"))).toBe(false);
  });
  it.each(["club", "camp"])("keeps unavailable %s management and deletion accessible", async kind => {
    data.clubs[0].isBotUnavailable = true;
    data.camps[0].isBotUnavailable = true;
    await mount();
    if (kind === "camp") await switchTo("camp-3");
    expect(host.textContent).toContain("Бот недоступен в Telegram-группе");
    await click("Удалить из OyinQ");
    expect(writes()).toContainEqual([`/admin/${kind === "club" ? "clubs/1" : "camps/3"}`, expect.objectContaining({ method: "DELETE" })]);
  });

  it("uses the shared photo cards and updates the selected chat photo", async () => {
    data.clubs[0].avatarUrl = "data:image/jpeg;base64,AQ==";
    data.camps[0].avatarUrl = "data:image/jpeg;base64,Ag==";
    await mount();
    expect(host.querySelector<HTMLImageElement>('.admin-community-trigger .community-avatar')?.src).toBe(data.clubs[0].avatarUrl);
    const trigger = host.querySelector<HTMLButtonElement>('button[aria-haspopup="listbox"]')!;
    await act(async () => trigger.click());
    const cards = host.querySelectorAll('.community-picker .card.community-option');
    expect(cards).toHaveLength(4);
    expect(cards[0].querySelector<HTMLImageElement>('.community-avatar')?.src).toBe(data.clubs[0].avatarUrl);
    expect(cards[1].querySelector('.mode-icon.club')).not.toBeNull();
    expect(cards[2].querySelector('small')?.textContent).toBe("Кэмп");
    await act(async () => (cards[2] as HTMLButtonElement).click());
    expect(host.querySelector<HTMLImageElement>('.admin-community-trigger .community-avatar')?.src).toBe(data.camps[0].avatarUrl);
    const photo = host.querySelector<HTMLImageElement>('.admin-community-trigger .community-avatar')!;
    await act(async () => photo.dispatchEvent(new Event("error")));
    expect(host.querySelector('.admin-community-trigger .mode-icon.camp')).not.toBeNull();
    expect(title()).toBe("Кэмп 3");
  });

  it("supports keyboard navigation and dismissing the themed menu without changing context", async () => {
    await mount();
    const trigger = host.querySelector<HTMLButtonElement>('button[aria-haspopup="listbox"]')!;
    await act(async () => { trigger.focus(); trigger.click(); });
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
    expect(document.activeElement?.getAttribute("aria-selected")).toBe("true");
    await act(async () => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    expect((document.activeElement as HTMLButtonElement).value).toBe("club-2");
    expect(title()).toBe("Клуб 1");
    await act(async () => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(host.querySelector('[role="listbox"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
    await act(async () => trigger.click());
    await act(async () => document.body.dispatchEvent(new Event("pointerdown", { bubbles: true })));
    expect(trigger.getAttribute("aria-expanded")).toBe("false");
    expect(title()).toBe("Клуб 1");
  });

  it("keeps admin selection separate, excludes locked chats, and restores the selected context", async () => {
    localStorage.setItem("oyinq-community", "camp-4");
    await mount();
    expect(title()).toBe("Клуб 1");
    const trigger = host.querySelector<HTMLButtonElement>('button[aria-haspopup="listbox"]')!;
    await act(async () => trigger.click());
    expect(host.querySelectorAll('[role="option"]')).toHaveLength(4);
    expect(host.querySelector(".admin-community-select")?.textContent).not.toContain("Закрытый клуб");
    await act(async () => trigger.click());
    expect(host.querySelector(".admin-discovery")?.textContent).toContain("Новая группа");
    await switchTo("camp-3");
    await click("Экспорт");
    await click("Сообщество");
    expect(title()).toBe("Кэмп 3");
    expect(localStorage.getItem("oyinq-community")).toBe("camp-4");
    expect(localStorage.getItem("oyinq-admin-community")).toBe("camp-3");
    await act(async () => root.unmount()); root = createRoot(host);
    await mount(); expect(title()).toBe("Кэмп 3");
  });

  it("automatically selects the only managed community and replaces an unavailable saved selection", async () => {
    data.clubs = []; data.camps = [data.camps[1]]; data.isSuperAdmin = false;
    localStorage.setItem("oyinq-admin-community", "locked");
    await mount();
    expect(host.querySelector('button[aria-haspopup="listbox"]')).toBeNull();
    expect(host.querySelector(".admin-community-current")?.textContent).toContain("Кэмп 4 · Кэмп");
    expect(localStorage.getItem("oyinq-admin-community")).toBe("camp-4");
  });

  it("preserves locked discovery and creation when there are no managed communities", async () => {
    data.clubs = []; data.camps = [];
    await mount();
    expect(host.textContent).toContain("Нет доступных сообществ");
    expect(host.textContent).toContain("Доступ не выдан");
    expect(host.textContent).toContain("Создать клуб");
    expect(localStorage.getItem("oyinq-admin-community")).toBeNull();
  });

  it("keeps settings open across clubs and camps, resets form values, and saves only the new target", async () => {
    await mount(); await click("Настройки");
    await switchTo("club-2");
    expect(title()).toBe("Настройки клуба");
    expect(host.querySelector<HTMLInputElement>('input[maxlength="160"]')?.value).toBe("Клуб 2");
    await switchTo("camp-3");
    expect(title()).toBe("Настройки кэмпа");
    expect(host.querySelector<HTMLInputElement>('input[maxlength="160"]')?.value).toBe("Кэмп 3");
    expect(api).toHaveBeenCalledWith("/admin/communities/camp-3/recruitment", undefined);
    await click("Сохранить настройки");
    expect(writes().map(([path]) => path)).toEqual(["/admin/camps/3"]);
    expect(title()).toBe("Кэмп 3");
  });

  it("keeps collection and participant screens for compatible communities and falls back for a different mode", async () => {
    await mount(); await click("Коллекция"); await switchTo("club-2");
    expect(title()).toBe("Коллекция");
    expect(api).toHaveBeenCalledWith("/admin/clubs/2/collection", undefined);
    await click("Скачать JSON");
    expect(download).toHaveBeenCalledWith("/admin/clubs/2/collection/export", "club-2.json");
    await switchTo("camp-3"); expect(title()).toBe("Кэмп 3");
    await click("Участники"); await switchTo("camp-4");
    expect(title()).toBe("Участники кэмпа");
    expect(api).toHaveBeenCalledWith("/admin/camps/4/participants", undefined);
    await switchTo("club-1"); expect(title()).toBe("Клуб 1");
  });

  it("refreshes gatherings, statistics and exports in the selected community", async () => {
    await mount(); await click("Сборы"); await switchTo("camp-3");
    expect(title()).toBe("Контроль сборов");
    expect(api).toHaveBeenCalledWith("/gatherings/dashboard?community=camp-3");
    await click("Экспорт"); await switchTo("club-2");
    expect(title()).toBe("Экспорт"); await click("Скачать «Клуб 2»");
    expect(download).toHaveBeenCalledWith("/admin/exports/statistics.zip?community=club-2", "oyinq-club-2-statistics.zip");
    await click("Скачать все чаты");
    expect(download).toHaveBeenCalledWith("/admin/exports/statistics.zip", "oyinq-all-statistics.zip");
  });

  it("ignores old administrator responses even after switching away and back", async () => {
    const pending = deferred<unknown>(); let first = true;
    intercept = path => {
      if (path === "/admin/communities/club-1/administrators" && first) { first = false; return pending.promise; }
    };
    await mount(); await click("Администраторы"); await switchTo("camp-3");
    expect(title()).toBe("Администраторы");
    expect(host.textContent).toContain("Админ camp-3");
    await switchTo("club-1");
    await act(async () => pending.resolve([{ telegramUserId: 55, displayName: "Устаревший ответ" }]));
    expect(host.textContent).toContain("Админ club-1");
    expect(host.textContent).not.toContain("Устаревший ответ");
  });

  it("does not submit a confirmation from a previous context", async () => {
    const pending = deferred<boolean>(); vi.mocked(telegram.confirm).mockReturnValue(pending.promise);
    await mount(); await click("Администраторы"); await click("Отозвать доступ");
    await switchTo("camp-3");
    await act(async () => pending.resolve(true));
    expect(writes()).toEqual([]);
    expect(title()).toBe("Администраторы");
    expect(telegram.success).not.toHaveBeenCalled();
  });

  it("does not apply late settings completion to the replacement screen", async () => {
    const pending = deferred<unknown>();
    intercept = (path, options) => path === "/admin/clubs/1" && options?.method === "PUT" ? pending.promise : undefined;
    await mount(); await click("Настройки"); await click("Сохранить настройки"); await switchTo("club-2");
    await act(async () => pending.resolve({}));
    expect(writes().map(([path]) => path)).toEqual(["/admin/clubs/1"]);
    expect(title()).toBe("Настройки клуба");
    expect(host.querySelector<HTMLInputElement>('input[maxlength="160"]')?.value).toBe("Клуб 2");
    expect(telegram.success).not.toHaveBeenCalled();
  });

  it("stops a native Telegram selection after switching context", async () => {
    const pending = deferred<boolean>(); vi.mocked(telegram.requestPeer).mockReturnValue(pending.promise);
    intercept = path => path === "/admin/peer-selections" ? Promise.resolve({ publicId: "ticket-a", preparedButtonId: "button-a" }) : undefined;
    await mount(); await click("Администраторы"); await click("Добавить администратора");
    await switchTo("camp-3");
    await act(async () => pending.resolve(false));
    expect(writes().map(([path]) => path)).toEqual(["/admin/peer-selections"]);
    expect(title()).toBe("Администраторы");
  });

  it("keeps global announcement drafts while showing the persistent switcher", async () => {
    await mount(); await click("Оповещения");
    const input = host.querySelector<HTMLInputElement>('input[aria-label="Текст оповещения"]')!;
    input.value = "Мой черновик";
    vi.mocked(telegram.back).mockClear();
    await switchTo("camp-3");
    expect(telegram.back).not.toHaveBeenCalled();
    expect(host.querySelector('input[aria-label="Текст оповещения"]')).toBe(input);
    expect(input.value).toBe("Мой черновик");
    await click("Сообщество"); expect(title()).toBe("Кэмп 3");
  });
});
