// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { ProfileDeletion, DeletedProfilePage } from "./ProfileDeletion";
import { api } from "../../api/client";
import { telegram } from "../../telegram/webApp";
import { profileDeletedEvent } from "../../app/profileLifecycle";
vi.mock("../../api/client", () => ({ api: vi.fn(), json: (method: string, body: unknown) => ({ method, body: JSON.stringify(body) }) }));
vi.mock("../../telegram/webApp", () => ({ telegram: { confirm: vi.fn(), openLink: vi.fn() } }));
let host: HTMLDivElement; let root: Root;
beforeEach(() => {
  vi.clearAllMocks(); Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  localStorage.clear(); sessionStorage.clear();
  vi.mocked(telegram.confirm).mockResolvedValue(true); vi.mocked(api).mockResolvedValue(undefined);
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });
const button = () => host.querySelector<HTMLButtonElement>("button")!;
it("requires confirmation and sends only a self-deletion request", async () => {
  const erased = vi.fn(); window.addEventListener(profileDeletedEvent, erased);
  try {
    localStorage.setItem("oyinq-profile-import", "private-job"); localStorage.setItem("unrelated", "keep");
    sessionStorage.setItem("oyinq-profile-tab:global", "settings");
    await act(async () => root.render(<ProfileDeletion />));
    expect(host.textContent).toContain("во всех сообществах"); expect(host.textContent).toContain("сборы, которые вы организуете, отменятся");
    vi.mocked(telegram.confirm).mockResolvedValueOnce(false);
    await act(async () => button().click()); expect(api).not.toHaveBeenCalled(); expect(erased).not.toHaveBeenCalled();
    await act(async () => button().click());
    expect(api).toHaveBeenCalledExactlyOnceWith("/profile", { method: "DELETE", body: JSON.stringify({ confirmed: true }) });
    expect(erased).toHaveBeenCalledOnce(); expect(localStorage.getItem("oyinq-profile-import")).toBeNull();
    expect(sessionStorage.getItem("oyinq-profile-tab:global")).toBeNull(); expect(localStorage.getItem("unrelated")).toBe("keep");
  } finally { window.removeEventListener(profileDeletedEvent, erased); }
});
it("preserves the profile and allows a retry after deletion fails", async () => {
  const erased = vi.fn(); window.addEventListener(profileDeletedEvent, erased);
  try {
    localStorage.setItem("oyinq-profile-import", "private-job");
    vi.mocked(api).mockRejectedValueOnce(new Error("Не удалось удалить профиль"));
    await act(async () => root.render(<ProfileDeletion />)); await act(async () => button().click());
    expect(host.textContent).toContain("Не удалось удалить профиль"); expect(button().disabled).toBe(false);
    expect(erased).not.toHaveBeenCalled(); expect(localStorage.getItem("oyinq-profile-import")).toBe("private-job");
    await act(async () => button().click()); expect(erased).toHaveBeenCalledOnce();
  } finally { window.removeEventListener(profileDeletedEvent, erased); }
});
it("does not create a new profile without an explicit confirmation", async () => {
  const recreated = vi.fn(); await act(async () => root.render(<DeletedProfilePage onRecreated={recreated} />));
  expect(api).not.toHaveBeenCalled();
  vi.mocked(telegram.confirm).mockResolvedValueOnce(false); await act(async () => button().click());
  expect(api).not.toHaveBeenCalled();
  await act(async () => button().click());
  expect(api).toHaveBeenCalledExactlyOnceWith("/profile/recreate", { method: "POST", body: JSON.stringify({ confirmed: true }) });
  expect(recreated).toHaveBeenCalledOnce(); expect(location.search).toBe("?tab=profile");
});
it("prevents duplicate deletion requests while confirmation or deletion is pending", async () => {
  let resolve!: () => void; vi.mocked(api).mockImplementation(() => new Promise<void>(done => { resolve = done; }));
  await act(async () => root.render(<ProfileDeletion />)); await act(async () => { button().click(); button().click(); });
  expect(api).toHaveBeenCalledOnce(); expect(button().disabled).toBe(true);
  await act(async () => resolve());
});
