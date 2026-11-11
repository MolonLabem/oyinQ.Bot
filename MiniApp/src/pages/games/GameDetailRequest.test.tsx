// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
vi.mock("../../api/client", async original => ({ ...await original<object>(), api: vi.fn() }));
vi.mock("../../telegram/webApp", () => ({ telegram: { back: vi.fn() }, successEventName: "success" }));
vi.mock("../../components/Wishlist", () => ({ WishButton: () => null, WishlistPanel: () => null }));
import { api } from "../../api/client";
import { GamesPage } from "./GamesPage";

let host: HTMLDivElement; let root: Root;
const game = { bggId: 233078, name: "Twilight Imperium", minPlayers: 3, maxPlayers: 6,
  typeNames: [], categories: [], mechanics: [], expansions: [], availability: { providers: [] } };
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); vi.clearAllMocks();
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  vi.mocked(api).mockImplementation(async path => {
    const url = new URL(path, "https://example.test");
    if (url.searchParams.has("attendanceDate") && !url.searchParams.get("attendanceDate")) throw new Error("Invalid empty date");
    return url.pathname === "/catalog/233078" ? game : { items: [game], filters: { types: [], categories: [], providers: [] } };
  });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });

it.each(["Club", "Camp"] as const)("opens %s game details without sending an empty date", async mode => {
  await act(async () => root.render(<GamesPage community={{ key: "test", name: "Community", mode, timeZoneId: "UTC" }} initialGameId={233078} bggAvailable={false} />));
  expect(host.textContent).toContain("Twilight Imperium");
  expect(host.textContent).not.toContain("Invalid empty date");
  expect(api).toHaveBeenCalledWith("/catalog/233078?community=test");
});

it.each([0, 2])("prioritizes existing gatherings only when available (count=%s)", async count => {
  Element.prototype.scrollIntoView = vi.fn();
  const create = vi.fn(); const open = vi.fn();
  vi.mocked(api).mockImplementation(async path => path.startsWith("/catalog/233078") ? { ...game, scheduledGatherings: count }
    : path.startsWith("/gatherings?") ? { items: count ? [{ card: { publicId: "existing", localDateTime: "14 ноября, 13:00", statusText: "Набор игроков", occupiedSeats: 2, maximumPlayers: 4 } }] : [] }
    : { items: [], filters: { types: [], categories: [], providers: [] } });
  await act(async () => root.render(<GamesPage community={{ key: "join", name: "Клуб", mode: "Club", timeZoneId: "UTC" }} initialGameId={233078} bggAvailable={false} createGathering={create} openGathering={open} />));
  const primary = host.querySelector<HTMLButtonElement>(".game-overview-actions .primary")!;
  expect(primary.textContent).toBe(count ? "Посмотреть сборы · 2" : "Создать сбор по этой игре");
  await act(async () => primary.click());
  if (count) {
    const section = host.querySelector('[role="region"][aria-label="Сборы по этой игре"]')!;
    expect(document.activeElement).toBe(section);
    expect(section.compareDocumentPosition(host.querySelector(".game-activity")!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    await act(async () => section.querySelector<HTMLButtonElement>("button.card")!.click());
    expect(open).toHaveBeenCalledWith("existing"); expect(create).not.toHaveBeenCalled();
  } else expect(create).toHaveBeenCalledWith(233078);
});
