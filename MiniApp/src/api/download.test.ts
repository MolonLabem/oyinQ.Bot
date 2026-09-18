// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
vi.mock("../telegram/webApp", () => ({ telegram: { initData: "signed-test-session" } }));
import { download } from "./client";

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal("fetch", vi.fn());
  URL.createObjectURL = vi.fn(() => "blob:test-file");
  URL.revokeObjectURL = vi.fn();
});
afterEach(() => { vi.runAllTimers(); vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe("authenticated downloads", () => {
  it("uses the server UTF-8 filename, authentication header and non-cacheable fetch", async () => {
    const fileName = "Участники-Кэмп-2026-09-19.xlsx";
    vi.mocked(fetch).mockResolvedValue(new Response("file", { headers: { "Content-Disposition": `attachment; filename="fallback.xlsx"; filename*=UTF-8''${encodeURIComponent(fileName)}` } }));
    let anchor: HTMLAnchorElement | undefined;
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) { anchor = this; });
    const controller = new AbortController();
    await download("/admin/camps/1/participants/export/xlsx?scope=all", "fallback.xlsx", controller.signal);
    expect(fetch).toHaveBeenCalledWith("/api/miniapp/admin/camps/1/participants/export/xlsx?scope=all", {
      headers: { "X-Telegram-Init-Data": "signed-test-session" }, signal: controller.signal, cache: "no-store",
    });
    expect(anchor?.download).toBe(fileName);
    expect(document.querySelector('a[href="blob:test-file"]')).toBeNull();
    expect(URL.revokeObjectURL).not.toHaveBeenCalled();
    vi.runAllTimers(); expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:test-file");
  });
  it("preserves an authorization error and never creates a download", async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify({ message: "Нет доступа к участникам этого кэмпа.", code: "forbidden" }), { status: 403 }));
    await expect(download("/admin/camps/1/participants/export/csv", "file.csv")).rejects.toMatchObject({ status: 403, code: "forbidden", message: "Нет доступа к участникам этого кэмпа." });
    expect(URL.createObjectURL).not.toHaveBeenCalled();
  });
  it("does not download a response after its screen was closed", async () => {
    const controller = new AbortController(); controller.abort();
    vi.mocked(fetch).mockResolvedValue(new Response("file"));
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    await download("/test", "file.csv", controller.signal);
    expect(click).not.toHaveBeenCalled(); expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:test-file");
  });
});
