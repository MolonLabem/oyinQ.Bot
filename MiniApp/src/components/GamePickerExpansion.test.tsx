// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { BggDetails } from "../api/types";

vi.mock("../api/client", () => ({ api: vi.fn() }));
import { api } from "../api/client";
import { GamePicker } from "./GamePicker";

let root: Root;
let host: HTMLDivElement;
const details: BggDetails = {
  game: { bggId: 134342, name: "Lords of Waterdeep: Scoundrels of Skullport", itemType: "Expansion", expansions: [] },
  expansions: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.useFakeTimers();
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  host = document.createElement("div");
  document.body.append(host);
  root = createRoot(host);
  vi.mocked(api).mockImplementation(async <T,>(path: string): Promise<T> => {
    if (path.startsWith("/bgg/search")) return [{ bggId: 134342, name: details.game.name }] as T;
    if (path.endsWith("&allowExpansions=true")) return details as T;
    throw new Error("Запрошенные данные не найдены.");
  });
});

afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.useRealTimers();
});

it.each([
  { allowExpansions: true, viaSearch: true },
  { allowExpansions: true, viaSearch: false },
  { allowExpansions: false, viaSearch: true },
  { allowExpansions: false, viaSearch: false },
])("opens expansions only when enabled: %o", async ({ allowExpansions, viaSearch }) => {
  const selected = vi.fn();
  await act(async () => root.render(<GamePicker bggAvailable allowExpansions={allowExpansions} onSelect={selected} />));
  const input = host.querySelector("input")!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input,
      viaSearch ? "Scoundrels" : "https://boardgamegeek.com/boardgame/134342/scoundrels");
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
  if (viaSearch) {
    await act(async () => vi.advanceTimersByTimeAsync(400));
    const result = host.querySelector<HTMLButtonElement>('[role="option"]');
    expect(result).not.toBeNull();
    await act(async () => result!.click());
  } else {
    const find = Array.from(host.querySelectorAll("button")).find(button => button.textContent === "Найти")!;
    await act(async () => find.click());
  }
  if (allowExpansions) {
    expect(selected).toHaveBeenCalledWith(details.game, "bgg");
    expect(host.textContent).not.toContain("Запрошенные данные не найдены.");
  } else {
    expect(selected).not.toHaveBeenCalled();
    expect(host.textContent).toContain("Запрошенные данные не найдены.");
  }
});
