// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { ClubCollectionState, ClubGame } from "../../api/types";
vi.mock("../../api/client", async original => ({ ...await original<typeof import("../../api/client")>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn(() => () => {}), success: vi.fn() }, successEventName: "success" }));
vi.mock("../../components/GamePicker", async original => ({
  ...await original<typeof import("../../components/GamePicker")>(),
  GamePicker: ({ onSelect }: { onSelect: (game: ClubGame, source: string, ids: number[]) => void }) => <div>
    <button onClick={() => onSelect({ bggId: 1, name: "Existing", expansions: [{ bggId: 11, name: "First" }, { bggId: 12, name: "Second" }] }, "bgg", [])}>Выбрать существующую</button>
    <button onClick={() => onSelect({ bggId: 2, name: "New game", expansions: [] }, "bgg", [])}>Выбрать новую</button>
  </div>,
}));
import { ApiError, api } from "../../api/client";
import { telegram } from "../../telegram/webApp";
import { ClubCollection } from "./ClubCollection";

let root: Root; let host: HTMLDivElement; let collection: ClubCollectionState;
let progress: { publicId: string; status: string; progressCurrent: number; progressTotal: number; updatedGames?: number };
const writes = () => vi.mocked(api).mock.calls.filter(([path, options]) => path.endsWith('/games') && options?.method === "POST");
async function click(text: string) {
  const button = Array.from(host.querySelectorAll('button')).find(item => item.textContent?.trim() === text);
  expect(button, text).toBeTruthy(); await act(async () => button!.click());
}
async function mount() { await act(async () => root.render(<ClubCollection clubId={1} bggAvailable back={() => {}} />)); }
async function tick() { await act(async () => { await vi.advanceTimersByTimeAsync(3000); }); }
beforeEach(() => {
  vi.useFakeTimers(); vi.clearAllMocks(); Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.spyOn(window, 'scrollTo').mockImplementation(() => {});
  HTMLElement.prototype.scrollIntoView = vi.fn();
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value() { this.open = true; } });
  Object.defineProperty(HTMLDialogElement.prototype, 'close', { configurable: true, value() { this.open = false; } });
  collection = { revision: 10, updatedAt: '2026-09-01T00:00:00Z', canEdit: true, collection: { version: 2, games: [{ bggId: 1, name: 'Existing', expansions: [{ bggId: 11, name: 'First' }] }] } };
  progress = { publicId:'job', status:'Running', progressCurrent:1, progressTotal:3 };
  vi.mocked(api).mockImplementation(async (path, options) => {
    if (path.includes('metadata-refresh')) return { ...progress };
    if (options?.method === 'POST') {
      const body = JSON.parse(String(options.body));
      if (body.expectedRevision !== collection.revision) throw new ApiError('Коллекция изменена другим администратором.', 409, 'stale_revision');
      if (body.bggInput === '2') collection = { ...collection, revision:11, collection: { version:2, games:[...collection.collection.games,{ bggId:2,name:'New game',expansions:[] }] } };
    }
    return structuredClone(collection);
  });
  host = document.createElement('div'); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.useRealTimers(); vi.restoreAllMocks(); });

it('has a primary add action, a collapsed maintenance area, and closes on Escape without writing', async () => {
  await mount();
  expect(host.querySelector('.club-add-action')?.classList.contains('primary')).toBe(true);
  expect(host.querySelector('details.club-collection-tools')?.hasAttribute('open')).toBe(false);
  expect(host.querySelector('dialog')).toBeNull();
  await click('+ Добавить игру');
  expect(host.querySelector('dialog')?.hasAttribute('open')).toBe(true);
  await act(async () => host.querySelector('dialog')!.dispatchEvent(new Event('cancel', { cancelable:true })));
  expect(host.querySelector('dialog')).toBeNull(); expect(writes()).toHaveLength(0);
});
it('adds through the existing endpoint, closes, reloads and reveals the added game', async () => {
  await mount(); await click('+ Добавить игру'); await click('Выбрать новую'); await click('Добавить в коллекцию');
  expect(JSON.parse(String(writes()[0][1]?.body))).toEqual({ expectedRevision:10, bggInput:'2', expansionBggIds:[] });
  expect(host.querySelector('dialog')).toBeNull(); expect(host.querySelector('input[type=search]')).toHaveProperty('value','New game');
  expect(host.querySelector('.collection-row h3')?.textContent).toBe('New game');
  expect(telegram.success).toHaveBeenCalledWith('Игра добавлена в коллекцию');
});
it('recognizes existing games, disables no-op and submits the exact edited expansion membership', async () => {
  await mount(); await click('+ Добавить игру'); await click('Выбрать существующую');
  expect(host.querySelector('dialog')?.textContent).toContain('Уже в коллекции');
  const save = Array.from(host.querySelectorAll('button')).find(button => button.textContent === 'Сохранить изменения')!;
  expect(save.disabled).toBe(true); await click('Сохранить изменения'); expect(writes()).toHaveLength(0);
  const boxes = host.querySelectorAll<HTMLInputElement>('dialog input[type=checkbox]');
  expect(boxes[0].checked).toBe(true); expect(boxes[1].checked).toBe(false);
  await act(async () => { boxes[0].click(); boxes[1].click(); });
  await click('Сохранить изменения');
  expect(JSON.parse(String(writes()[0][1]?.body)).expansionBggIds).toEqual([12]);
  expect(telegram.success).toHaveBeenCalledWith('Изменения сохранены');
});
it.each([0,2])('shows refresh progress and final changed count %i separately from revision', async changed => {
  await mount(); await click('Обновить данные из BGG');
  expect(host.textContent).toContain('Обновляем данные: 1 из 3');
  progress = { ...progress, status:'Completed',progressCurrent:3,updatedGames:changed };
  await tick(); expect(host.textContent).toContain(changed ? 'Обновлено: 2 игры' : 'Данные уже актуальны');
});
it('keeps the draft revision during a background publication and requires reload after conflict', async () => {
  await mount(); await click('Обновить данные из BGG'); await click('+ Добавить игру'); await click('Выбрать существующую');
  await act(async () => host.querySelectorAll<HTMLInputElement>('dialog input[type=checkbox]')[1].click());
  collection = { ...collection, revision:11 }; progress = { ...progress,status:'Completed',updatedGames:1 };
  await tick(); await click('Сохранить изменения');
  expect(JSON.parse(String(writes()[0][1]?.body)).expectedRevision).toBe(10);
  expect(host.querySelector('dialog')?.textContent).toContain('Коллекция изменена');
  await click('Загрузить актуальную коллекцию'); expect(host.querySelector('dialog')?.textContent).not.toContain('Изменений нет');
});
