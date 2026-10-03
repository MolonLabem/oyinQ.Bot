// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import { api } from "./client";
import { profileDeletedEvent, profileWasRecreated } from "../app/profileLifecycle";
afterEach(() => vi.unstubAllGlobals());
const gone = () => new Response(JSON.stringify({ code: "profile_deleted", message: "Профиль удалён" }), { status: 410 });
it("signals deletion for an authenticated session that has already been erased", async () => {
  const erased = vi.fn(); window.addEventListener(profileDeletedEvent, erased);
  try {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(gone()));
    await expect(api("/communities")).rejects.toMatchObject({ status: 410, code: "profile_deleted" });
    expect(erased).toHaveBeenCalledOnce();
  } finally { window.removeEventListener(profileDeletedEvent, erased); }
});
it("ignores a late deletion response from a session before explicit recreation", async () => {
  const erased = vi.fn(); window.addEventListener(profileDeletedEvent, erased);
  try {
    let resolve!: (response: Response) => void;
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => new Promise<Response>(done => { resolve = done; })));
    const pending = api("/profile"); profileWasRecreated(); resolve(gone());
    await expect(pending).rejects.toMatchObject({ status: 410 }); expect(erased).not.toHaveBeenCalled();
  } finally { window.removeEventListener(profileDeletedEvent, erased); }
});
